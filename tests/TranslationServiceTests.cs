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
    [Fact]
    public void TestLocalDictionaryTranslator_DirectBuiltIn()
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
    public void TestLocalDictionaryTranslator_WordByWordFallback()
    {
        var translator = new LocalDictionaryTranslator();

        // Compound word translation (target = "fa")
        Assert.Equal("فایل تنظیمات", translator.Translate("File Settings", "fa"));

        // Mixture of glossary words and unknown words (which remain untranslated)
        Assert.Equal("فایل Unknown تنظیمات", translator.Translate("File Unknown settings", "fa"));
    }

    [Fact]
    public void TestLocalDictionaryTranslator_CustomDictionary()
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
    public async Task TestTranslationService_CacheAndOfflineState()
    {
        // Clear any existing cache file to ensure complete test isolation
        var cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinLens", "translation_cache.json");
        if (File.Exists(cachePath))
        {
            try { File.Delete(cachePath); } catch { /* ignore */ }
        }

        // Create an isolated SettingsService
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

        // Turn on local dict, translate a word to cache it
        settings.EnableLocalDictionary = true;
        var cachedResult = await translationService.TranslateAsync("Cancel", "fa");
        Assert.Equal("لغو", cachedResult);

        // Even with local dict off, cached values should be resolved instantly from cache!
        settings.EnableLocalDictionary = false;
        var cachedResolved = await translationService.TranslateAsync("Cancel", "fa");
        Assert.Equal("لغو", cachedResolved);
    }
}
