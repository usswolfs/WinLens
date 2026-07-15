using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinLens.Services;

public sealed class LocalDictionaryTranslator
{
    private readonly string _dictsDir;
    private readonly Dictionary<string, Dictionary<string, string>> _loadedDicts = new(StringComparer.OrdinalIgnoreCase);

    // Rich built-in glossary for offline translations to support multiple target languages.
    private static readonly Dictionary<string, Dictionary<string, string>> BuiltInGlossary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fa"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = "تایید",
            ["cancel"] = "لغو",
            ["save"] = "ذخیره",
            ["close"] = "بستن",
            ["settings"] = "تنظیمات",
            ["file"] = "فایل",
            ["edit"] = "ویرایش",
            ["error"] = "خطا",
            ["success"] = "موفقیت",
            ["exit"] = "خروج",
            ["yes"] = "بله",
            ["no"] = "خیر",
            ["help"] = "راهنما",
            ["loading"] = "در حال بارگذاری",
            ["search"] = "جستجو",
            ["warning"] = "هشدار",
            ["options"] = "گزینه‌ها",
            ["view"] = "مشاهده",
            ["tools"] = "ابزارها",
            ["window"] = "پنجره",
            ["about"] = "درباره",
            ["apply"] = "اعمال",
            ["run"] = "اجرا",
            ["information"] = "اطلاعات",
            ["offline"] = "آفلاین",
            ["online"] = "آنلاین"
        },
        ["zh"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = "确定",
            ["cancel"] = "取消",
            ["save"] = "保存",
            ["close"] = "关闭",
            ["settings"] = "设置",
            ["file"] = "文件",
            ["edit"] = "编辑",
            ["error"] = "错误",
            ["success"] = "成功",
            ["exit"] = "退出",
            ["yes"] = "是",
            ["no"] = "否",
            ["help"] = "帮助",
            ["loading"] = "加载中",
            ["search"] = "搜索",
            ["warning"] = "警告",
            ["options"] = "选项",
            ["view"] = "查看",
            ["tools"] = "工具",
            ["window"] = "窗口",
            ["about"] = "关于",
            ["apply"] = "应用",
            ["run"] = "运行",
            ["information"] = "信息",
            ["offline"] = "离线",
            ["online"] = "在线"
        },
        ["ja"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = "了解",
            ["cancel"] = "キャンセル",
            ["save"] = "保存",
            ["close"] = "閉じる",
            ["settings"] = "設定",
            ["file"] = "ファイル",
            ["edit"] = "編集",
            ["error"] = "エラー",
            ["success"] = "成功",
            ["exit"] = "終了",
            ["yes"] = "はい",
            ["no"] = "いいえ",
            ["help"] = "ヘルプ",
            ["loading"] = "読み込み中",
            ["search"] = "検索",
            ["warning"] = "警告",
            ["options"] = "オプション",
            ["view"] = "表示",
            ["tools"] = "ツール",
            ["window"] = "ウィンドウ",
            ["about"] = "バージョン情報",
            ["apply"] = "適用",
            ["run"] = "実行",
            ["information"] = "情報",
            ["offline"] = "オフライン",
            ["online"] = "オンライン"
        },
        ["ko"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = "확인",
            ["cancel"] = "취소",
            ["save"] = "저장",
            ["close"] = "닫기",
            ["settings"] = "설정",
            ["file"] = "파일",
            ["edit"] = "편집",
            ["error"] = "오류",
            ["success"] = "성공",
            ["exit"] = "종료",
            ["yes"] = "예",
            ["no"] = "아니오",
            ["help"] = "도움말",
            ["loading"] = "로딩 중",
            ["search"] = "검색",
            ["warning"] = "경고",
            ["options"] = "옵션",
            ["view"] = "보기",
            ["tools"] = "도구",
            ["window"] = "창",
            ["about"] = "정보",
            ["apply"] = "적용",
            ["run"] = "실행",
            ["information"] = "정보",
            ["offline"] = "오프라인",
            ["online"] = "온라인"
        },
        ["es"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = "Aceptar",
            ["cancel"] = "Cancelar",
            ["save"] = "Guardar",
            ["close"] = "Cerrar",
            ["settings"] = "Configuración",
            ["file"] = "Archivo",
            ["edit"] = "Editar",
            ["error"] = "Error",
            ["success"] = "Éxito",
            ["exit"] = "Salir",
            ["yes"] = "Sí",
            ["no"] = "No",
            ["help"] = "Ayuda",
            ["loading"] = "Cargando",
            ["search"] = "Buscar",
            ["warning"] = "Advertencia",
            ["options"] = "Opciones",
            ["view"] = "Ver",
            ["tools"] = "Herramientas",
            ["window"] = "Ventana",
            ["about"] = "Acerca de",
            ["apply"] = "Aplicar",
            ["run"] = "Ejecutar",
            ["information"] = "Información",
            ["offline"] = "Desconectado",
            ["online"] = "En línea"
        },
        ["fr"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = "D'accord",
            ["cancel"] = "Annuler",
            ["save"] = "Enregistrer",
            ["close"] = "Fermer",
            ["settings"] = "Paramètres",
            ["file"] = "Fichier",
            ["edit"] = "Modifier",
            ["error"] = "Erreur",
            ["success"] = "Succès",
            ["exit"] = "Quitter",
            ["yes"] = "Oui",
            ["no"] = "Non",
            ["help"] = "Aide",
            ["loading"] = "Chargement",
            ["search"] = "Rechercher",
            ["warning"] = "Avertissement",
            ["options"] = "Options",
            ["view"] = "Afficher",
            ["tools"] = "Outils",
            ["window"] = "Fenêtre",
            ["about"] = "À propos",
            ["apply"] = "Appliquer",
            ["run"] = "Exécuter",
            ["information"] = "Informations",
            ["offline"] = "Hors ligne",
            ["online"] = "En ligne"
        },
        ["de"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = "OK",
            ["cancel"] = "Abbrechen",
            ["save"] = "Speichern",
            ["close"] = "Schließen",
            ["settings"] = "Einstellungen",
            ["file"] = "Datei",
            ["edit"] = "Bearbeiten",
            ["error"] = "Fehler",
            ["success"] = "Erfolgreich",
            ["exit"] = "Beenden",
            ["yes"] = "Ja",
            ["no"] = "Nein",
            ["help"] = "Hilfe",
            ["loading"] = "Laden",
            ["search"] = "Suchen",
            ["warning"] = "Warnung",
            ["options"] = "Optionen",
            ["view"] = "Ansicht",
            ["tools"] = "Werkzeuge",
            ["window"] = "Fenster",
            ["about"] = "Über",
            ["apply"] = "Anwenden",
            ["run"] = "Ausführen",
            ["information"] = "Information",
            ["offline"] = "Offline",
            ["online"] = "Online"
        }
    };

    public LocalDictionaryTranslator()
    {
        _dictsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinLens"
        );
    }

    private Dictionary<string, string> GetOrLoadDictionary(string targetLang)
    {
        var tgt = targetLang.Split('-')[0].ToLowerInvariant();

        lock (_loadedDicts)
        {
            if (_loadedDicts.TryGetValue(tgt, out var dict))
                return dict;

            // Initialize with built-in glossary if available
            var newDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (BuiltInGlossary.TryGetValue(tgt, out var builtIn))
            {
                foreach (var kvp in builtIn)
                    newDict[kvp.Key] = kvp.Value;
            }

            // Also add inverse mappings if English is target
            if (tgt == "en")
            {
                // Map from other languages back to English
                foreach (var langGlossary in BuiltInGlossary)
                {
                    foreach (var kvp in langGlossary.Value)
                    {
                        // kvp.Key is English, kvp.Value is foreign translation
                        if (!newDict.ContainsKey(kvp.Value))
                            newDict[kvp.Value] = kvp.Key;
                    }
                }
            }

            // Attempt to load external custom file: offline_dict_[lang].json
            var filePath = Path.Combine(_dictsDir, $"offline_dict_{tgt}.json");
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var external = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (external != null)
                    {
                        foreach (var kvp in external)
                        {
                            newDict[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch
            {
                // Ignore load errors for corrupted custom files
            }

            _loadedDicts[tgt] = newDict;
            return newDict;
        }
    }

    public string Translate(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var dict = GetOrLoadDictionary(targetLang);
        var trimmed = text.Trim();

        // 1. Direct whole phrase translation (case-insensitive)
        if (dict.TryGetValue(trimmed, out var translatedWhole))
            return translatedWhole;

        // Strip basic punctuation and try again
        var cleaned = Regex.Replace(trimmed, @"[^\w\s]", "").Trim();
        if (dict.TryGetValue(cleaned, out var translatedCleaned))
            return translatedCleaned;

        // 2. Word-by-word / sub-phrase translation fallback
        var tokens = Regex.Split(text, @"(\b\w+\b)");
        var result = new System.Text.StringBuilder();

        foreach (var token in tokens)
        {
            if (Regex.IsMatch(token, @"^\w+$"))
            {
                if (dict.TryGetValue(token, out var translatedWord))
                    result.Append(translatedWord);
                else
                    result.Append(token);
            }
            else
            {
                result.Append(token);
            }
        }

        return result.ToString();
    }
}
