using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace WinLens.Services;

/// <summary>
/// Translates short strings. Tries Google gtx first (auto source detection),
/// falls back to MyMemory. Logs each failure to %TEMP%\winlens.log.
/// Now supports fully features offline translation via Argos Translate / NLLB models
/// and Local Dictionary.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private const string GoogleEndpoint   = "https://translate.googleapis.com/translate_a/single";
    private const string MyMemoryEndpoint = "https://api.mymemory.translated.net/get";

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<(string text, string tgt), string> _cache = new();
    private readonly string _logPath;
    private readonly string _cachePath;
    private readonly SettingsService? _settingsService;
    private readonly LocalDictionaryTranslator _localTranslator = new();
    private readonly OfflineModelTranslator _offlineModelTranslator = new();

    public OfflineModelTranslator OfflineModel => _offlineModelTranslator;

    public TranslationService(SettingsService? settingsService = null)
    {
        _settingsService = settingsService;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WinLens/1.0");
        _logPath = Path.Combine(Path.GetTempPath(), "winlens.log");
        _cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinLens", "translation_cache.json");
        LoadCacheFromDisk();
    }

    public static bool IsOnline()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        }
        catch
        {
            return false;
        }
    }

    private void LoadCacheFromDisk()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                var json = File.ReadAllText(_cachePath, Encoding.UTF8);
                var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (deserialized != null)
                {
                    foreach (var kvp in deserialized)
                    {
                        var parts = kvp.Key.Split(new[] { "||" }, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            _cache[(parts[0], parts[1])] = kvp.Value;
                        }
                    }
                }
                Log($"Loaded {_cache.Count} translation cache entries from disk.");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to load cache from disk: {ex.Message}");
        }
    }

    private readonly object _cacheFileLock = new();

    private void SaveCacheToDisk()
    {
        lock (_cacheFileLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_cachePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var serialized = new Dictionary<string, string>();
                foreach (var kvp in _cache)
                {
                    var keyStr = $"{kvp.Key.text}||{kvp.Key.tgt}";
                    serialized[keyStr] = kvp.Value;
                }

                var json = JsonSerializer.Serialize(serialized);
                File.WriteAllText(_cachePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log($"Failed to save cache to disk: {ex.Message}");
            }
        }
    }

    public async Task<string> TranslateAsync(
        string text,
        string targetLang,
        string? sourceLang = null,
        CancellationToken ct = default)
    {
        _ = sourceLang;

        if (string.IsNullOrWhiteSpace(text))
            return text;

        var tgt = targetLang.Split('-')[0];
        var key = (text, tgt);

        var sw = Stopwatch.StartNew();

        // 1. Instant Cache Hit Check (< 1 ms)
        if (_cache.TryGetValue(key, out var cached))
        {
            Log($"[Cache Hit] '{text}' -> '{cached}' (tgt: {tgt}) in {sw.Elapsed.TotalMilliseconds:F2} ms");
            return cached;
        }

        bool forceOffline = _settingsService?.Current?.ForceOffline ?? false;
        bool useLocalDict = _settingsService?.Current?.EnableLocalDictionary ?? true;
        string preferredEngine = _settingsService?.Current?.PreferredOfflineEngine ?? "Auto";

        bool activeOffline = forceOffline || !IsOnline();

        if (activeOffline)
        {
            Log($"[Offline Translation Active] PreferredEngine: {preferredEngine}, UseLocalDict: {useLocalDict}");
            var result = ExecuteOfflineTranslation(text, tgt, preferredEngine, useLocalDict);
            if (result != null)
            {
                _cache[key] = result;
                SaveCacheToDisk();
                Log($"[Offline Translation Success] '{text}' -> '{result}' (tgt: {tgt}) via {preferredEngine} in {sw.Elapsed.TotalMilliseconds:F2} ms");
                return result;
            }
            Log($"[Offline Translation Miss/No Match] '{text}' returning original");
            return text;
        }

        // Online mode: try Google Translate
        Log($"[Online Translation] Trying Google Translate for '{text}' (tgt: {tgt})");
        var google = await TryGoogleAsync(text, tgt, ct);
        if (google != null)
        {
            _cache[key] = google;
            SaveCacheToDisk();
            Log($"[Google Success] '{text}' -> '{google}' in {sw.Elapsed.TotalMilliseconds:F2} ms");
            return google;
        }

        // Fallback to MyMemory
        Log($"[Online Translation] Google failed, trying MyMemory for '{text}'");
        var memory = await TryMyMemoryAsync(text, "en", tgt, ct);
        if (memory != null)
        {
            _cache[key] = memory;
            SaveCacheToDisk();
            Log($"[MyMemory Success] '{text}' -> '{memory}' in {sw.Elapsed.TotalMilliseconds:F2} ms");
            return memory;
        }

        // Online services failed - Fallback to Offline engines gracefully
        Log($"[Online Translation Failed] Falling back to offline engines for '{text}'");
        var fallbackOffline = ExecuteOfflineTranslation(text, tgt, preferredEngine, useLocalDict);
        if (fallbackOffline != null)
        {
            _cache[key] = fallbackOffline;
            SaveCacheToDisk();
            Log($"[Offline Fallback Success] '{text}' -> '{fallbackOffline}' via {preferredEngine} in {sw.Elapsed.TotalMilliseconds:F2} ms");
            return fallbackOffline;
        }

        Log($"[All Translation Engines Failed] '{text}' returning original");
        return text;
    }

    private string? ExecuteOfflineTranslation(string text, string targetLang, string preferredEngine, bool useLocalDict)
    {
        if (string.Equals(preferredEngine, "Argos", StringComparison.OrdinalIgnoreCase))
        {
            if (_offlineModelTranslator.IsModelDownloaded())
            {
                var argosResult = _offlineModelTranslator.Translate(text, targetLang);
                if (argosResult != null)
                    return argosResult;
            }
            else
            {
                Log("[Argos Warning] Argos engine preferred but model en-fa.json not downloaded.");
            }

            // Fallback to LocalDictionary if permitted
            if (useLocalDict)
            {
                var localResult = _localTranslator.Translate(text, targetLang);
                if (!string.Equals(localResult, text, StringComparison.OrdinalIgnoreCase))
                    return localResult;
            }
        }
        else if (string.Equals(preferredEngine, "LocalDictionary", StringComparison.OrdinalIgnoreCase))
        {
            if (useLocalDict)
            {
                var localResult = _localTranslator.Translate(text, targetLang);
                if (!string.Equals(localResult, text, StringComparison.OrdinalIgnoreCase))
                    return localResult;
            }
        }
        else // Auto selection
        {
            // If model is downloaded, try Argos first, otherwise LocalDictionary
            if (_offlineModelTranslator.IsModelDownloaded())
            {
                var argosResult = _offlineModelTranslator.Translate(text, targetLang);
                if (argosResult != null)
                    return argosResult;
            }

            if (useLocalDict)
            {
                var localResult = _localTranslator.Translate(text, targetLang);
                if (!string.Equals(localResult, text, StringComparison.OrdinalIgnoreCase))
                    return localResult;
            }
        }

        return null;
    }

    private async Task<string?> TryGoogleAsync(string text, string tgt, CancellationToken ct)
    {
        var url = $"{GoogleEndpoint}?client=gtx&sl=auto&tl={tgt}&dt=t&q={HttpUtility.UrlEncode(text)}";
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Log($"google http {(int)resp.StatusCode} for tgt={tgt}");
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!body.StartsWith('['))
            {
                Log($"google non-json body (first 80 chars): {body.AsSpan(0, Math.Min(80, body.Length)).ToString()}");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return null;

            var sentences = root[0];
            if (sentences.ValueKind != JsonValueKind.Array)
                return null;

            var sb = new StringBuilder();
            foreach (var s in sentences.EnumerateArray())
            {
                if (s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0 &&
                    s[0].ValueKind == JsonValueKind.String)
                {
                    sb.Append(s[0].GetString());
                }
            }
            var result = sb.ToString();
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch (Exception ex)
        {
            Log($"google exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> TryMyMemoryAsync(string text, string src, string tgt, CancellationToken ct)
    {
        if (string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase))
            return text;

        var url = $"{MyMemoryEndpoint}?q={HttpUtility.UrlEncode(text)}&langpair={src}|{tgt}";
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Log($"mymemory http {(int)resp.StatusCode} for {src}|{tgt}");
                return null;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            if (doc.RootElement.TryGetProperty("responseData", out var data) &&
                data.TryGetProperty("translatedText", out var translated))
            {
                var output = translated.GetString();
                return string.IsNullOrEmpty(output) ? null : output;
            }
            return null;
        }
        catch (Exception ex)
        {
            Log($"mymemory exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void Log(string line)
    {
        try
        {
            File.AppendAllText(_logPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }

    public void Dispose() => _http.Dispose();
}
