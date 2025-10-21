using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml;
using System.Reflection.Metadata;

namespace XmlTranslationTool
{
    public class AppConfig
    {
        public string? ApiKey { get; set; } = string.Empty;
        public string? Model { get; set; } = "deepseek-ai/DeepSeek-R1-0528-Qwen3-8B";
        public string? ApiUrl { get; set; } = "https://api.deepseek.com/v1/chat/completions";
        public string? TargetLanguage { get; set; } = "zh-CN"; // 默认简体中文
        public string? LastSelectedPath { get; set; } = string.Empty;
        public string TranslationMarker { get; set; } = "<!-- AI-Translated -->";
        public string UILanguage { get; set; } = "zh-CN"; // 默认界面语言为中文

        public static AppConfig Load()
        {
            string configPath = "config.json";
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                catch
                {
                    return new AppConfig();
                }
            }
            return new AppConfig();
        }

        public void Save()
        {
            string configPath = "config.json";
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }
    }

    public class TranslationService
    {
        private readonly HttpClient _httpClient;
        private readonly AppConfig _config;
        private readonly Action<string> _logAction;

        public TranslationService(AppConfig config, Action<string> logAction = null)
        {
            _config = config;
            _httpClient = new HttpClient();
            _logAction = logAction ?? Console.WriteLine;

            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
            }
        }

        public async Task<string> TranslateTextAsync(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(_config.ApiKey))
                {
                    _logAction("错误：API密钥未设置，请在配置中设置API密钥。");
                    return string.Empty;
                }

                // 根据目标语言生成系统提示
                string systemPrompt = GetSystemPrompt(_config.TargetLanguage);

                var requestData = new
                {
                    model = _config.Model,
                    messages = new[]
                    {
                    new
                    {
                        role = "system",
                        content = systemPrompt
                    },
                    new
                    {
                        role = "user",
                        content = text
                    }
                },
                    max_tokens = 4000,
                    temperature = 0.3,
                    stream = false
                };

                string json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logAction($"正在调用翻译API (目标语言: {_config.TargetLanguage})...");
                HttpResponseMessage response = await _httpClient.PostAsync(_config.ApiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    if (responseObj.TryGetProperty("choices", out JsonElement choices) &&
                        choices.GetArrayLength() > 0)
                    {
                        var message = choices[0].GetProperty("message");
                        if (message.TryGetProperty("content", out JsonElement contentElement))
                        {
                            string translated = contentElement.GetString()?.Trim() ?? string.Empty;
                            _logAction("翻译成功！");
                            return translated;
                        }
                    }

                    _logAction("API响应格式异常");
                    return string.Empty;
                }
                else
                {
                    _logAction($"API调用失败: {response.StatusCode}");
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _logAction($"错误详情: {errorContent}");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logAction($"翻译过程中出错: {ex.Message}");
                return string.Empty;
            }
        }

        // 根据目标语言生成系统提示
        private string GetSystemPrompt(string targetLanguage)
        {
            var languagePrompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["zh-CN"] = "你是一个专业的翻译助手。请将以下英文文本准确翻译成简体中文，保持原有的格式、换行符和特殊符号不变。不要添加任何额外的解释或内容。",
                ["zh-TW"] = "你是一個專業的翻譯助手。請將以下英文文本準確翻譯成繁體中文，保持原有的格式、換行符和特殊符號不變。不要添加任何額外的解釋或內容。",
                ["ja"] = "あなたはプロの翻訳アシスタントです。以下の英語テキストを正確な日本語に翻訳し、元のフォーマット、改行、特殊記号を保持してください。余分な説明や内容を追加しないでください。",
                ["ko"] = "당신은 전문 번역 도우미입니다. 다음 영어 텍스트를 정확한 한국어로 번역하고 원본 형식, 줄 바꿈 및 특수 기호를 유지하세요. 추가 설명이나 내용을 덧붙이지 마세요。",
                ["fr"] = "Vous êtes un assistant de traduction professionnel. Veuillez traduire avec précision le texte anglais suivant en français, en conservant le format d'origine, les sauts de ligne et les symboles spéciaux. N'ajoutez aucune explication ou contenu supplémentaire.",
                ["de"] = "Sie sind ein professioneller Übersetzungsassistent. Bitte übersetzen Sie den folgenden englischen Text genau ins Deutsche und behalten Sie das ursprüngliche Format, Zeilenumbrüche und Sonderzeichen bei. Fügen Sie keine zusätzlichen Erklärungen oder Inhalte hinzu.",
                ["es"] = "Eres un asistente de traducción profesional. Por favor, traduce con precisión el siguiente texto en inglés al español, manteniendo el formato original, los saltos de línea y los símbolos especiales. No añadas ninguna explicación o contenido adicional.",
                ["ru"] = "Вы профессиональный помощник по переводу. Пожалуйста, точно переведите следующий английский текст на русский язык, сохраняя исходный формат, разрывы строк и специальные символы. Не добавляйте никаких дополнительных объяснений или содержания。",
                ["pt"] = "Você é um assistente de tradução profissional. Por favor, traduza com precisão o seguinte texto em inglês para português, mantendo o formato original, quebras de linha e símbolos especiais. Não adicione nenhuma explicação ou conteúdo adicional.",
                ["it"] = "Sei un assistente di traduzione professionale. Si prega di tradurre accuratamente il seguente testo inglese in italiano, mantenendo il formato originale, gli interruzioni di riga e i simboli speciali. Non aggiungere spiegazioni o contenuti aggiuntivi.",
                ["ar"] = "أنت مساعد ترجمة محترف. يرجى ترجمة النص الإنجليزي التالي بدقة إلى العربية، مع الحفاظ على التنسيق الأصلي، وفواصل الأسطر، والرموز الخاصة. لا تضيف أي تفسيرات أو محتوى إضافي.",
                ["hi"] = "आप एक पेशेवर अनुवाद सहायक हैं। कृपया निम्नलिखित अंग्रेजी पाठ का सटीक हिंदी में अनुवाद करें, मूल स्वरूप, लाइन ब्रेक और विशेष प्रतीकों को बनाए रखें। कोई अतिरिक्त स्पष्टीकरण या सामग्री न जोड़ें。",
                ["tr"] = "Profesyonel bir çeviri asistanısınız. Lütfen aşağıdaki İngilizce metni Türkçeye doğru bir şekilde çevirin, orijinal biçimi, satır sonlarını ve özel sembolleri koruyun. Ek açıklama veya içerik eklemeyin.",
                ["vi"] = "Bạn là một trợ lý dịch thuật chuyên nghiệp. Hãy dịch chính xác văn bản tiếng Anh sau sang tiếng Việt, giữ nguyên định dạng gốc, ngắt dòng và ký tự đặc biệt. Không thêm bất kỳ giải thích hoặc nội dung bổ sung nào。",
                ["th"] = "คุณเป็นผู้ช่วยแปลมืออาชีพ กรุณาแปลข้อความภาษาอังกฤษต่อไปนี้เป็นภาษาไทยอย่างถูกต้อง โดยคงรูปแบบเดิม การขึ้นบรรทัดใหม่ และสัญลักษณ์พิเศษไว้ อย่าเพิ่มคำอธิบายหรือเนื้อหาเพิ่มเติม",
                ["pl"] = "Jesteś profesjonalnym asystentem tłumaczenia. Proszę dokładnie przetłumaczyć poniższy tekst angielski na język polski, zachowując oryginalny format, podziały wierszy i symbole specjalne. Nie dodawaj żadnych dodatkowych wyjaśnień ani treści."
            };

            // 如果找到对应的语言提示，使用它；否则使用通用提示
            if (languagePrompts.TryGetValue(targetLanguage, out string prompt))
            {
                return prompt;
            }

            // 通用提示，使用用户指定的目标语言
            return $"You are a professional translation assistant. Please accurately translate the following English text into {targetLanguage}, maintaining the original format, line breaks, and special symbols. Do not add any additional explanations or content.";
        }
    }

    public static class Localization
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _resources = new()
        {
            ["zh-CN"] = new Dictionary<string, string>
            {
                ["ApiKeyNotSet"] = "错误：API密钥未设置，请在配置中设置API密钥。",
                ["AppTitle"] = "about.xml 文件翻译工具",
                ["SelectOperation"] = "请选择操作:",
                ["SelectPath"] = "选择路径并处理about.xml文件",
                ["ConfigSettings"] = "配置设置",
                ["Exit"] = "退出",
                ["EnterChoice"] = "请输入选择 (1-3): ",
                ["CurrentConfig"] = "当前配置:",
                ["Model"] = "模型",
                ["ApiKey"] = "API密钥",
                ["ApiUrl"] = "API地址",
                ["TargetLanguage"] = "目标语言",
                ["TranslationMarker"] = "翻译标记",
                ["LastPath"] = "上次路径",
                ["NotSet"] = "未设置",
                ["IsSet"] = "已设置",
                ["ConfigSettingsTitle"] = "=== 配置设置 ===",
                ["ReturnMainMenu"] = "返回主菜单",
                ["EnterOption"] = "请输入要修改的选项编号 (1-6): ",
                ["ConfigSaved"] = "配置已保存！",
                ["InvalidChoice"] = "无效选择，请重新输入。",
                ["SelectFolder"] = "请选择文件夹路径:",
                ["UseLastPath"] = "使用上次路径",
                ["EnterNewPath"] = "输入新路径",
                ["ManualBrowse"] = "手动浏览（输入文件夹路径）",
                ["EnterChoicePath"] = "请选择 (1-3): ",
                ["EnterValidPath"] = "请输入有效的文件夹路径: ",
                ["PathNotSelected"] = "未选择路径。",
                ["StartProcessing"] = "开始处理路径: {0}",
                ["ProcessingFiles"] = "找到 {0} 个about.xml文件",
                ["ProcessingFile"] = "[{0}/{1}] 处理文件: {2}",
                ["SkippedTranslated"] = "跳过已翻译文件",
                ["ProcessingComplete"] = "=== 处理完成！ ===",
                ["Total"] = "总计",
                ["Success"] = "成功",
                ["Skipped"] = "跳过",
                ["Failed"] = "失败",
                ["SelectModel"] = "选择模型:",
                ["DeepSeekModel"] = "deepseek-ai/DeepSeek-R1-0528-Qwen3-8B",
                ["QwenModel"] = "Qwen/Qwen3-8B",
                ["SelectModelPrompt"] = "请选择 (1-2): ",
                ["ModelSwitched"] = "已切换到 {0} 模型。",
                ["EnterApiKey"] = "请输入新的API密钥: ",
                ["ApiKeyUpdated"] = "API密钥已更新。",
                ["EnterApiUrl"] = "请输入API地址 (默认: {0}): ",
                ["ApiUrlUpdated"] = "API地址已更新。",
                ["LanguageCodes"] = "常见语言代码:",
                ["EnterTargetLanguage"] = "请输入目标语言代码 (默认: {0}): ",
                ["TargetLanguageUpdated"] = "目标语言已更新。",
                ["EnterTranslationMarker"] = "请输入翻译标记 (默认: {0}): ",
                ["TranslationMarkerUpdated"] = "翻译标记已更新。",
                ["SelectUILanguage"] = "选择界面语言:",
                ["Chinese"] = "中文",
                ["English"] = "English",
                ["UILanguageUpdated"] = "界面语言已更新。",
                ["FileLocked"] = "文件被占用，无法处理。",
                ["BackupCreated"] = "已创建备份: {0}",
                ["NoDescription"] = "未找到description内容，跳过此文件",
                ["DescriptionLength"] = "提取到description内容，长度: {0} 字符",
                ["OriginalPreview"] = "原文预览: {0}",
                ["StartTranslation"] = "开始翻译...",
                ["TranslationSuccess"] = "翻译成功！",
                ["TranslationFailed"] = "翻译失败，跳过此文件",
                ["FileUpdated"] = "文件更新完成，已添加翻译标记",
                ["CallingApi"] = "正在调用翻译API (目标语言: {0})...",
                ["ApiCallFailed"] = "API调用失败: {0}",
                ["ErrorDetails"] = "错误详情: {0}",
                ["TranslationError"] = "翻译过程中出错: {0}",
                ["FileProcessError"] = "处理文件时出错: {0}",
                ["XmlParseFailed"] = "XML解析失败，使用正则表达式回退: {0}",
                ["XmlProcessFailed"] = "XML处理失败，使用正则表达式回退: {0}",
                ["NoDescriptionTags"] = "警告：替换后未找到description标签，使用回退方法"
            },
            ["en"] = new Dictionary<string, string>
            {
                ["ApiKeyNotSet"] = "Error: API key is not set, please set it in configuration.",
                ["AppTitle"] = "about.xml File Translation Tool",
                ["SelectOperation"] = "Please select an operation:",
                ["SelectPath"] = "Select path and process about.xml files",
                ["ConfigSettings"] = "Configuration settings",
                ["Exit"] = "Exit",
                ["EnterChoice"] = "Please enter your choice (1-3): ",
                ["CurrentConfig"] = "Current configuration:",
                ["Model"] = "Model",
                ["ApiKey"] = "API Key",
                ["ApiUrl"] = "API URL",
                ["TargetLanguage"] = "Target language",
                ["TranslationMarker"] = "Translation marker",
                ["LastPath"] = "Last path",
                ["NotSet"] = "Not set",
                ["IsSet"] = "Set",
                ["ConfigSettingsTitle"] = "=== Configuration Settings ===",
                ["ReturnMainMenu"] = "Return to main menu",
                ["EnterOption"] = "Please enter the option number to modify (1-6): ",
                ["ConfigSaved"] = "Configuration saved!",
                ["InvalidChoice"] = "Invalid choice, please try again.",
                ["SelectFolder"] = "Please select folder path:",
                ["UseLastPath"] = "Use last path",
                ["EnterNewPath"] = "Enter new path",
                ["ManualBrowse"] = "Manual browse (enter folder path)",
                ["EnterChoicePath"] = "Please select (1-3): ",
                ["EnterValidPath"] = "Please enter a valid folder path: ",
                ["PathNotSelected"] = "Path not selected.",
                ["StartProcessing"] = "Start processing path: {0}",
                ["ProcessingFiles"] = "Found {0} about.xml files",
                ["ProcessingFile"] = "[{0}/{1}] Processing file: {2}",
                ["SkippedTranslated"] = "Skipped translated file",
                ["ProcessingComplete"] = "=== Processing complete! ===",
                ["Total"] = "Total",
                ["Success"] = "Success",
                ["Skipped"] = "Skipped",
                ["Failed"] = "Failed",
                ["SelectModel"] = "Select model:",
                ["DeepSeekModel"] = "deepseek-ai/DeepSeek-R1-0528-Qwen3-8B",
                ["QwenModel"] = "Qwen/Qwen3-8B",
                ["SelectModelPrompt"] = "Please select (1-2): ",
                ["ModelSwitched"] = "Switched to {0} model.",
                ["EnterApiKey"] = "Please enter new API key: ",
                ["ApiKeyUpdated"] = "API key updated.",
                ["EnterApiUrl"] = "Please enter API URL (default: {0}): ",
                ["ApiUrlUpdated"] = "API URL updated.",
                ["LanguageCodes"] = "Common language codes:",
                ["EnterTargetLanguage"] = "Please enter target language code (default: {0}): ",
                ["TargetLanguageUpdated"] = "Target language updated.",
                ["EnterTranslationMarker"] = "Please enter translation marker (default: {0}): ",
                ["TranslationMarkerUpdated"] = "Translation marker updated.",
                ["SelectUILanguage"] = "Select UI language:",
                ["Chinese"] = "中文",
                ["English"] = "English",
                ["UILanguageUpdated"] = "UI language updated.",
                ["FileLocked"] = "File is locked, cannot process.",
                ["BackupCreated"] = "Backup created: {0}",
                ["NoDescription"] = "No description content found, skipping file",
                ["DescriptionLength"] = "Extracted description content, length: {0} characters",
                ["OriginalPreview"] = "Original preview: {0}",
                ["StartTranslation"] = "Starting translation...",
                ["TranslationSuccess"] = "Translation successful!",
                ["TranslationFailed"] = "Translation failed, skipping file",
                ["FileUpdated"] = "File updated successfully, translation marker added",
                ["CallingApi"] = "Calling translation API (target language: {0})...",
                ["ApiCallFailed"] = "API call failed: {0}",
                ["ErrorDetails"] = "Error details: {0}",
                ["TranslationError"] = "Error during translation: {0}",
                ["FileProcessError"] = "Error processing file: {0}",
                ["XmlParseFailed"] = "XML parsing failed, using regex fallback: {0}",
                ["XmlProcessFailed"] = "XML processing failed, using regex fallback: {0}",
                ["NoDescriptionTags"] = "Warning: No description tags found after replacement, using fallback method"
            }
        };

        public static string GetString(string key, string uiLanguage = "zh-CN")
        {
            if (_resources.TryGetValue(uiLanguage, out var languageResources) &&
                languageResources.TryGetValue(key, out var value))
            {
                return value;
            }

            // Fallback to English if not found
            if (uiLanguage != "en" && _resources["en"].TryGetValue(key, out var englishValue))
            {
                return englishValue;
            }

            return key; // Return the key itself if not found
        }

        public static string GetString(string key, string uiLanguage, params object[] args)
        {
            string template = GetString(key, uiLanguage);
            return string.Format(template, args);
        }
    }

    class Program
    {
        private const string BackupExtension = ".bak";
        private static AppConfig _config = null!;
        private static TranslationService _translationService = null!;
        private static StreamWriter _logWriter = null!; // 添加这行
        private const string LogFileName = "translation_tool.log";

        // 添加日志方法
        private static void LogMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logMessage = $"[{timestamp}] {message}";

            // 输出到控制台
            Console.WriteLine(message);

            // 同时写入日志文件
            _logWriter?.WriteLine(logMessage);
            _logWriter?.Flush(); // 确保立即写入
        }

        static async Task Main(string[] args)
        {
            // 初始化日志
            InitializeLog();

            // 设置控制台编码
            Console.OutputEncoding = Encoding.UTF8;

            // 先加载配置，然后再使用
            _config = AppConfig.Load();

            // 初始化 TranslationService
            _translationService = new TranslationService(_config, LogMessage);

            LogMessage(Localization.GetString("AppTitle", _config.UILanguage));
            LogMessage("=====================");

            _translationService = new TranslationService(_config);

            // 显示当前配置
            ShowCurrentConfig();

            // 主循环
            while (true)
            {
                ShowMenu();
                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await SelectAndProcessPath();
                        break;
                    case "2":
                        ConfigureSettings();
                        break;
                    case "3":
                        LogMessage(Localization.GetString("Exit", _config.UILanguage));
                        CloseLog();
                        return;
                    default:
                        LogMessage(Localization.GetString("InvalidChoice", _config.UILanguage));
                        break;
                }

                LogMessage(""); // 空行分隔
            }
        }

        // 初始化日志文件
        private static void InitializeLog()
        {
            try
            {
                // 如果日志文件已存在，删除它（每次启动清理旧日志）
                if (File.Exists(LogFileName))
                {
                    File.Delete(LogFileName);
                }

                // 创建新的日志文件
                _logWriter = new StreamWriter(LogFileName, false, Encoding.UTF8);
                LogMessage($"日志文件已初始化: {Path.GetFullPath(LogFileName)}");
            }
            catch (Exception ex)
            {
                LogMessage($"初始化日志文件失败: {ex.Message}");
            }
        }

        // 关闭日志文件
        private static void CloseLog()
        {
            try
            {
                _logWriter?.Close();
                _logWriter = null;
            }
            catch (Exception ex)
            {
                LogMessage($"关闭日志文件失败: {ex.Message}");
            }
        }

        static void ShowCurrentConfig()
        {
            LogMessage(Localization.GetString("CurrentConfig", _config.UILanguage));
            LogMessage($"  {Localization.GetString("Model", _config.UILanguage)}: {_config.Model}");
            LogMessage($"  {Localization.GetString("ApiKey", _config.UILanguage)}: {(string.IsNullOrEmpty(_config.ApiKey) ? Localization.GetString("NotSet", _config.UILanguage) : Localization.GetString("IsSet", _config.UILanguage))}");
            LogMessage($"  {Localization.GetString("ApiUrl", _config.UILanguage)}: {_config.ApiUrl}");
            LogMessage($"  {Localization.GetString("TargetLanguage", _config.UILanguage)}: {_config.TargetLanguage}");
            LogMessage($"  {Localization.GetString("TranslationMarker", _config.UILanguage)}: {_config.TranslationMarker}");
            LogMessage($"  {Localization.GetString("UILanguage", _config.UILanguage)}: {_config.UILanguage}");
            if (!string.IsNullOrEmpty(_config.LastSelectedPath))
                LogMessage($"  {Localization.GetString("LastPath", _config.UILanguage)}: {_config.LastSelectedPath}");
        }

        static void ShowMenu()
        {
            LogMessage(Localization.GetString("SelectOperation", _config.UILanguage));
            LogMessage($"1. {Localization.GetString("SelectPath", _config.UILanguage)}");
            LogMessage($"2. {Localization.GetString("ConfigSettings", _config.UILanguage)}");
            LogMessage($"3. {Localization.GetString("Exit", _config.UILanguage)}");
            Console.Write(Localization.GetString("EnterChoice", _config.UILanguage));
        }

        static async Task SelectAndProcessPath()
        {
            string selectedPath = SelectFolderPath();

            if (string.IsNullOrEmpty(selectedPath))
            {
                LogMessage("未选择路径。");
                return;
            }

            _config.LastSelectedPath = selectedPath;
            _config.Save();

            LogMessage($"开始处理路径: {selectedPath}");
            await ProcessXmlFiles(selectedPath);
        }

        static string SelectFolderPath()
        {
            try
            {
                // 使用简单的控制台输入替代FolderBrowserDialog
                LogMessage($"\n{Localization.GetString("SelectFolder", _config.UILanguage)}");
                LogMessage($"1. {Localization.GetString("UseLastPath", _config.UILanguage)}: " + (string.IsNullOrEmpty(_config.LastSelectedPath) ? Localization.GetString("NotSet", _config.UILanguage) : _config.LastSelectedPath));
                LogMessage($"2. {Localization.GetString("EnterNewPath", _config.UILanguage)}");
                LogMessage($"3. {Localization.GetString("ManualBrowse", _config.UILanguage)}");
                Console.Write(Localization.GetString("EnterChoicePath", _config.UILanguage));

                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        if (!string.IsNullOrEmpty(_config.LastSelectedPath) && Directory.Exists(_config.LastSelectedPath))
                            return _config.LastSelectedPath;
                        else
                            LogMessage("上次路径无效或不存在。");
                        break;
                    case "2":
                        Console.Write("请输入新的默认路径: ");
                        var newPath = Console.ReadLine();
                        if (!string.IsNullOrEmpty(newPath) && Directory.Exists(newPath))
                            return newPath;
                        else
                            LogMessage("路径无效或不存在。");
                        break;
                    case "3":
                        Console.Write("请输入要处理的文件夹路径: ");
                        var manualPath = Console.ReadLine();
                        if (!string.IsNullOrEmpty(manualPath) && Directory.Exists(manualPath))
                            return manualPath;
                        else
                            LogMessage("路径无效或不存在。");
                        break;
                    default:
                        LogMessage("无效选择。");
                        break;
                }

                // 如果上述方法都失败，让用户重新输入
                Console.Write("请输入有效的文件夹路径: ");
                string path = Console.ReadLine() ?? "";
                if (Directory.Exists(path))
                    return path;
            }

            catch (Exception ex)
            {
                LogMessage(Localization.GetString("FileProcessError", _config.UILanguage, ex.Message));
            }

            return string.Empty;
        }

        static void ConfigureSettings()
        {
            LogMessage(Localization.GetString("ConfigSettingsTitle", _config.UILanguage));

            while (true)
            {
                LogMessage($"\n{Localization.GetString("CurrentConfig", _config.UILanguage)}");
                LogMessage($"1. {Localization.GetString("ApiKey", _config.UILanguage)}: {(string.IsNullOrEmpty(_config.ApiKey) ? Localization.GetString("NotSet", _config.UILanguage) : Localization.GetString("IsSet", _config.UILanguage))}");
                LogMessage($"2. {Localization.GetString("Model", _config.UILanguage)}: {_config.Model}");
                LogMessage($"3. {Localization.GetString("ApiUrl", _config.UILanguage)}: {_config.ApiUrl}");
                LogMessage($"4. {Localization.GetString("TargetLanguage", _config.UILanguage)}: {_config.TargetLanguage}");
                LogMessage($"5. {Localization.GetString("TranslationMarker", _config.UILanguage)}: {_config.TranslationMarker}");
                LogMessage($"6. {Localization.GetString("UILanguage", _config.UILanguage)}: {_config.UILanguage}");
                LogMessage($"7. {Localization.GetString("ReturnMainMenu", _config.UILanguage)}");

                Console.Write($"\n{Localization.GetString("EnterOption", _config.UILanguage)}".Replace("(1-6)", "(1-7)"));
                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        Console.Write(Localization.GetString("EnterApiKey", _config.UILanguage));
                        var newKey = Console.ReadLine();
                        if (!string.IsNullOrEmpty(newKey))
                        {
                            _config.ApiKey = newKey;
                            LogMessage(Localization.GetString("ApiKeyUpdated", _config.UILanguage));
                        }
                        break;
                    case "2":
                        LogMessage($"\n{Localization.GetString("SelectModel", _config.UILanguage)}");
                        LogMessage($"1. {Localization.GetString("DeepSeekModel", _config.UILanguage)}");
                        LogMessage($"2. {Localization.GetString("QwenModel", _config.UILanguage)}");
                        Console.Write(Localization.GetString("SelectModelPrompt", _config.UILanguage));
                        var modelChoice = Console.ReadLine();
                        if (modelChoice == "2")
                        {
                            _config.Model = "Qwen/Qwen3-8B";
                            LogMessage(Localization.GetString("ModelSwitched", _config.UILanguage, "Qwen/Qwen3-8B"));
                        }
                        else
                        {
                            _config.Model = "deepseek-ai/DeepSeek-R1-0528-Qwen3-8B";
                            LogMessage(Localization.GetString("ModelSwitched", _config.UILanguage, "DeepSeek-R1"));
                        }
                        break;
                    case "3":
                        Console.Write(Localization.GetString("EnterApiUrl", _config.UILanguage, _config.ApiUrl));
                        var newUrl = Console.ReadLine();
                        if (!string.IsNullOrEmpty(newUrl))
                        {
                            _config.ApiUrl = newUrl;
                            LogMessage(Localization.GetString("ApiUrlUpdated", _config.UILanguage));
                        }
                        break;
                    case "4":
                        LogMessage($"\n{Localization.GetString("LanguageCodes", _config.UILanguage)}");
                        LogMessage("  zh-CN - 简体中文 (Simplified Chinese)");
                        LogMessage("  zh-TW - 繁體中文 (Traditional Chinese)");
                        LogMessage("  ja - 日本語 (Japanese)");
                        LogMessage("  ko - 한국어 (Korean)");
                        LogMessage("  fr - Français (French)");
                        LogMessage("  de - Deutsch (German)");
                        LogMessage("  es - Español (Spanish)");
                        LogMessage("  ru - Русский (Russian)");
                        LogMessage("  pt - Português (Portuguese)");
                        LogMessage("  it - Italiano (Italian)");
                        LogMessage("  ar - العربية (Arabic)");
                        LogMessage("  hi - हिन्दी (Hindi)");
                        LogMessage("  tr - Türkçe (Turkish)");
                        LogMessage("  vi - Tiếng Việt (Vietnamese)");
                        LogMessage("  th - ภาษาไทย (Thai)");
                        LogMessage("  pl - Polski (Polish)");
                        Console.Write(Localization.GetString("EnterTargetLanguage", _config.UILanguage, _config.TargetLanguage));
                        var newLang = Console.ReadLine();
                        if (!string.IsNullOrEmpty(newLang))
                        {
                            _config.TargetLanguage = newLang;
                            LogMessage(Localization.GetString("TargetLanguageUpdated", _config.UILanguage));
                        }
                        break;
                    case "5":
                        Console.Write(Localization.GetString("EnterTranslationMarker", _config.UILanguage, _config.TranslationMarker));
                        var newMarker = Console.ReadLine();
                        if (!string.IsNullOrEmpty(newMarker))
                        {
                            _config.TranslationMarker = newMarker;
                            LogMessage(Localization.GetString("TranslationMarkerUpdated", _config.UILanguage));
                        }
                        break;
                    case "6":
                        LogMessage($"\n{Localization.GetString("SelectUILanguage", _config.UILanguage)}");
                        LogMessage($"1. {Localization.GetString("Chinese", _config.UILanguage)}");
                        LogMessage($"2. {Localization.GetString("English", _config.UILanguage)}");
                        Console.Write(Localization.GetString("SelectModelPrompt", _config.UILanguage));
                        var uiLangChoice = Console.ReadLine();
                        _config.UILanguage = uiLangChoice == "2" ? "en" : "zh-CN";
                        LogMessage(Localization.GetString("UILanguageUpdated", _config.UILanguage));
                        break;
                    case "7":
                        _config.Save();
                        _translationService = new TranslationService(_config);
                        LogMessage(Localization.GetString("ConfigSaved", _config.UILanguage));
                        return;
                    default:
                        LogMessage(Localization.GetString("InvalidChoice", _config.UILanguage));
                        break;
                }
            }
        }

        static async Task ProcessXmlFiles(string rootPath)
        {
            try
            {
                var xmlFiles = Directory.GetFiles(rootPath, "about.xml", SearchOption.AllDirectories);

                if (xmlFiles.Length == 0)
                {
                    LogMessage("未找到任何about.xml文件。");
                    return;
                }

                LogMessage($"找到 {xmlFiles.Length} 个about.xml文件");

                int processed = 0;
                int skipped = 0;
                int succeeded = 0;
                int failed = 0;

                foreach (string filePath in xmlFiles)
                {
                    processed++;

                    // 检查文件是否已翻译
                    bool isTranslated = IsFileAlreadyTranslated(filePath);
                    LogMessage($"文件 [{processed}/{xmlFiles.Length}]: {filePath} - 已翻译: {isTranslated}");

                    if (isTranslated)
                    {
                        LogMessage($"跳过已翻译文件: {filePath}");
                        skipped++;
                        continue;
                    }

                    LogMessage($"\n--- 处理文件: {filePath} ---");

                    bool success = await ProcessSingleFile(filePath);
                    if (success)
                        succeeded++;
                    else
                        failed++;

                    // 避免频繁调用API，添加延迟
                    await Task.Delay(500);
                }

                LogMessage($"\n=== 处理完成！ ===");
                LogMessage($"总计: {xmlFiles.Length}, 成功: {succeeded}, 跳过: {skipped}, 失败: {failed}");
            }
            catch (Exception ex)
            {
                LogMessage($"处理文件时发生错误: {ex.Message}");
            }
        }

        static bool IsFileAlreadyTranslated(string filePath)
        {
            try
            {
                string content = File.ReadAllText(filePath, Encoding.UTF8);

                // 多种方式检查标记
                bool hasExactMarker = content.Contains(_config.TranslationMarker);
                bool hasAITranslated = content.Contains("<!-- AI-Translated");
                bool hasTranslatedComment = content.Contains("<!-- Translated");

                // 如果有任意一种标记，都认为是已翻译
                bool isTranslated = hasExactMarker || hasAITranslated || hasTranslatedComment;

                // 调试信息 - 使用绝对路径
                if (isTranslated)
                {
                    LogMessage($"文件已标记为翻译: {filePath}");
                    if (hasExactMarker) LogMessage($"  - 精确匹配配置标记: {_config.TranslationMarker}");
                    if (hasAITranslated) LogMessage("  - 包含AI翻译标记");
                    if (hasTranslatedComment) LogMessage("  - 包含翻译注释");
                }

                return isTranslated;
            }
            catch (Exception ex)
            {
                LogMessage($"检查文件翻译状态时出错: {ex.Message}");
                return false;
            }
        }

        static async Task<bool> ProcessSingleFile(string filePath)
        {
            try
            {
                // 检查文件是否可写
                if (IsFileLocked(filePath))
                {
                    LogMessage("文件被占用，无法处理。");
                    return false;
                }

                // 备份原文件
                string backupPath = filePath + BackupExtension;
                File.Copy(filePath, backupPath, true);
                LogMessage($"已创建备份: {backupPath}");

                // 读取文件内容
                string content = File.ReadAllText(filePath, Encoding.UTF8);

                // 提取description内容
                string description = ExtractDescription(content);

                if (string.IsNullOrEmpty(description))
                {
                    LogMessage("未找到description内容，跳过此文件");
                    return false;
                }

                LogMessage($"提取到description内容，长度: {description.Length} 字符");

                // 显示部分原文（前100字符）
                if (description.Length > 100)
                {
                    LogMessage($"原文预览: {description.Substring(0, 100)}...");
                }
                else
                {
                    LogMessage($"原文预览: {description}");
                }

                LogMessage("开始翻译...");

                // 调用翻译
                string translatedText = await _translationService.TranslateTextAsync(description);

                if (string.IsNullOrEmpty(translatedText))
                {
                    LogMessage("翻译失败，跳过此文件");
                    return false;
                }

                LogMessage("翻译完成");

                // 替换原文件中的description内容
                string newContent = ReplaceDescription(content, translatedText);

                // 验证替换结果
                if (!newContent.Contains("<description>") && !newContent.Contains("</description>"))
                {
                    LogMessage("警告：替换后未找到description标签，使用回退方法");
                    newContent = ReplaceDescriptionFallback(content, translatedText);
                }

                // 添加翻译标记
                newContent = AddTranslationMarker(newContent);

                // 保存文件
                File.WriteAllText(filePath, newContent, Encoding.UTF8);
                LogMessage("文件更新完成，已添加翻译标记");

                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"处理文件时出错: {ex.Message}");
                return false;
            }
        }

        static bool IsFileLocked(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return true;
            }
            return false;
        }

        static string ExtractDescription(string xmlContent)
        {
            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlContent);

                var descriptionNode = xmlDoc.SelectSingleNode("//description");
                return descriptionNode?.InnerText?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogMessage($"XML解析失败，使用正则表达式回退: {ex.Message}");
                return ExtractDescriptionFallback(xmlContent);
            }
        }

        static string ReplaceDescription(string xmlContent, string newDescription)
        {
            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.PreserveWhitespace = true;
                xmlDoc.LoadXml(xmlContent);

                var descriptionNode = xmlDoc.SelectSingleNode("//description");
                if (descriptionNode != null)
                {
                    descriptionNode.InnerText = newDescription;

                    using var stringWriter = new StringWriter();
                    using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings
                    {
                        Indent = true,
                        IndentChars = "  ",
                        NewLineChars = "\n",
                        OmitXmlDeclaration = false,
                        Encoding = Encoding.UTF8
                    });

                    xmlDoc.Save(xmlWriter);
                    return stringWriter.ToString();
                }

                return xmlContent;
            }
            catch (Exception ex)
            {
                LogMessage($"XML处理失败，使用正则表达式回退: {ex.Message}");
                return ReplaceDescriptionFallback(xmlContent, newDescription);
            }
        }

        // 正则表达式回退方法
        static string ExtractDescriptionFallback(string xmlContent)
        {
            var match = Regex.Match(xmlContent,
                @"<description(\s[^>]*)?>([\s\S]*?)</description>",
                RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[2].Value.Trim() : string.Empty;
        }

        static string ReplaceDescriptionFallback(string xmlContent, string newDescription)
        {
            return Regex.Replace(xmlContent,
                @"<description(\s[^>]*)?>([\s\S]*?)</description>",
                match =>
                {
                    string startTag = match.Groups[1].Success ?
                        $"<description{match.Groups[1].Value}>" : "<description>";

                    return $"{startTag}{newDescription}</description>";
                },
                RegexOptions.IgnoreCase);
        }

        static string AddTranslationMarker(string xmlContent)
        {
            try
            {
                string marker = $"\n{_config.TranslationMarker}\n";

                // 移除任何现有的翻译标记
                xmlContent = Regex.Replace(xmlContent, @"<!--\s*AI-Translated[^>]*-->", "");
                xmlContent = Regex.Replace(xmlContent, @"<!--\s*Translated[^>]*-->", "");

                // 在 XML 声明后插入标记
                int insertPosition = xmlContent.IndexOf("?>");
                if (insertPosition > 0)
                {
                    insertPosition += 2; // 移动到 ?> 之后
                                         // 检查是否已经有换行
                    if (insertPosition < xmlContent.Length && xmlContent[insertPosition] != '\n')
                    {
                        marker = "\n" + marker.TrimStart();
                    }
                    return xmlContent.Insert(insertPosition, marker);
                }

                // 如果找不到 XML 声明，在文件开头添加
                return marker + xmlContent;
            }
            catch (Exception ex)
            {
                LogMessage($"添加翻译标记时出错: {ex.Message}");
                return xmlContent;
            }
        }
    }
}