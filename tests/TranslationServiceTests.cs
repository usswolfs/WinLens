using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WinLens.Models;
using WinLens.Services;
using Xunit;

namespace WinLens.Tests;

public class TranslationServiceTests
{
    private static void ClearPersistentCache()
    {
        var cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinLens", "translation_cache.json");
        if (File.Exists(cachePath))
        {
            try { File.Delete(cachePath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Test1_LocalDictionaryTranslator_DirectBuiltIn()
    {
        var translator = new LocalDictionaryTranslator();

        // Test basic Persian UI terms (target = "fa")
        Assert.Equal("تنظیمات", translator.Translate("Settings", "fa"));
        Assert.Equal("خطا", translator.Translate("Error", "fa"));
        Assert.Equal("لغو", translator.Translate("Cancel", "fa"));

        // Case insensitivity
        Assert.Equal("تنظیمات", translator.Translate("settings", "fa"));
        Assert.Equal("تنظیمات", translator.Translate("SETTINGS", "fa"));

        // Cleaning punctuation
        Assert.Equal("تنظیمات", translator.Translate("Settings!!!", "fa"));

        // Inverse mapping to English (target = "en")
        Assert.Equal("settings", translator.Translate("تنظیمات", "en"));
        Assert.Equal("error", translator.Translate("خطا", "en"));
    }

    [Fact]
    public void Test2_LocalDictionaryTranslator_WordByWordFallback()
    {
        var translator = new LocalDictionaryTranslator();

        // Compound word translation (target = "fa")
        Assert.Equal("فایل تنظیمات", translator.Translate("File Settings", "fa"));

        // Mixture of glossary words and unknown words (which remain untranslated)
        Assert.Equal("فایل Unknown تنظیمات", translator.Translate("File Unknown settings", "fa"));
    }

    [Fact]
    public void Test3_LocalDictionaryTranslator_CustomDictionary()
    {
        // Setup a custom dictionary in %APPDATA%/WinLens
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinLens");
        Directory.CreateDirectory(appDataDir);
        var customDictPath = Path.Combine(appDataDir, "offline_dict_es.json");

        var testDict = new System.Collections.Generic.Dictionary<string, string>
        {
            ["customword"] = "palabrapersonalizada"
        };
        File.WriteAllText(customDictPath, JsonSerializer.Serialize(testDict));

        try
        {
            var translator = new LocalDictionaryTranslator();
            Assert.Equal("palabrapersonalizada", translator.Translate("customword", "es"));
        }
        finally
        {
            if (File.Exists(customDictPath))
                File.Delete(customDictPath);
        }
    }

    [Fact]
    public async Task Test4_OfflineModelTranslator_DirectTranslation()
    {
        var offlineModel = new OfflineModelTranslator();

        // Download/generate model dataset
        await offlineModel.DownloadModelAsync();
        Assert.True(offlineModel.IsModelDownloaded());

        // Test high-quality en-fa translation matching
        var translatedHello = offlineModel.Translate("Hello", "fa");
        Assert.Equal("سلام", translatedHello);

        var translatedSettings = offlineModel.Translate("Settings", "fa");
        Assert.Equal("تنظیمات", translatedSettings);
    }

    [Fact]
    public async Task Test5_TranslationService_CacheHitPerformance()
    {
        ClearPersistentCache();

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        settings.ForceOffline = true;
        settings.EnableLocalDictionary = true;

        using var translationService = new TranslationService(settingsService);
        await translationService.OfflineModel.DownloadModelAsync();

        // Warm up and cache
        await translationService.TranslateAsync("Hello", "fa");

        // Cache hit timing check: should be extremely fast (under 1 millisecond)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cached = await translationService.TranslateAsync("Hello", "fa");
        sw.Stop();

        Assert.Equal("سلام", cached);
        Assert.True(sw.Elapsed.TotalMilliseconds < 5, "Cache hit must be ultra-fast (under 5 milliseconds).");
    }

    [Fact]
    public async Task Test6_TranslationService_ForceOfflineAndLocalDictionary()
    {
        ClearPersistentCache();

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        settings.ForceOffline = true;
        settings.EnableLocalDictionary = true;

        using var translationService = new TranslationService(settingsService);

        // This should trigger the offline/local glossary translation
        var result = await translationService.TranslateAsync("Settings", "fa");
        Assert.Equal("تنظیمات", result);

        // If local dictionary is disabled and we are forced offline, it should return the original text
        settings.EnableLocalDictionary = false;
        var resultNoDict = await translationService.TranslateAsync("Cancel", "fa");
        Assert.Equal("Cancel", resultNoDict);
    }

    [Fact]
    public async Task Test7_TranslationService_OfflineEnginePreference()
    {
        ClearPersistentCache();

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        settings.ForceOffline = true;
        settings.EnableLocalDictionary = true;

        // Ensure offline Argos model is ready
        using var translationService = new TranslationService(settingsService);
        await translationService.OfflineModel.DownloadModelAsync();

        // Test with "Argos" preferred
        settings.PreferredOfflineEngine = "Argos";
        var resultArgos = await translationService.TranslateAsync("Hello", "fa");
        Assert.Equal("سلام", resultArgos);

        // Test with "LocalDictionary" preferred
        settings.PreferredOfflineEngine = "LocalDictionary";
        var resultLocal = await translationService.TranslateAsync("Settings", "fa");
        Assert.Equal("تنظیمات", resultLocal);
    }

    [Fact]
    public async Task Test8_TranslationService_OnlineFallbackToOffline()
    {
        ClearPersistentCache();

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        settings.ForceOffline = false; // Online mode
        settings.EnableLocalDictionary = true;

        using var translationService = new TranslationService(settingsService);
        await translationService.OfflineModel.DownloadModelAsync();

        // When offline or if online fails, it should fallback to offline engines
        var result = await translationService.TranslateAsync("Hello", "fa");
        // Hello should always translate to سلام either via Google Translate online, or fallback to local en-fa offline model
        Assert.Equal("سلام", result);
    }
}
