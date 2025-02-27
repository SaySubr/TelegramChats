using Microsoft.AspNetCore.Localization;
using System.Configuration;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using static Telegram.Bot.TelegramBotClient;
using System.Text;
using Microsoft.Extensions.Configuration;


namespace TelegramChat
{
    internal class Program
    {
        private static TelegramBotClient _BotClient;
        private static Dictionary<long, string> _pendingSettingsChanges = new();
        static async Task Main(string[] args)
        {
            // Новый способ зашифровать телеграмм токена
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) 
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true) 
                .Build();

         
            string Token = configuration["TelegramBotToken"];

            
            _BotClient = new TelegramBotClient(Token);

            await SetBotCommands();
            var me = await _BotClient.GetMe();
            await SetBotCommands();

            var ResiveOption = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
                DropPendingUpdates = true
            };
            var cts = new CancellationTokenSource();

            _BotClient.StartReceiving(UpdateHandler, ErrorHandler, ResiveOption, cts.Token);

            Console.WriteLine($"Бот {me.Username}: Запущен...");
            while (true)
            {
                Console.WriteLine("Введите команду для остановки...Stop");
                var Command = Console.ReadLine();
                if (Command == "Stop")
                    break;
                else
                    Console.WriteLine("Команда не распознана!");
            }
        }
        
        private static async Task ErrorHandler(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken token)
        {
            try
            {
                var ErrorMessage = exception switch
                {
                    ApiRequestException apiRequestException
                    => $"Tellegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}.",
                    _ => exception.ToString()
                };
                Console.WriteLine($"Error: {ErrorMessage}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Program.ErrorHadler(Execption):{ex.Message}");
            }
        }

        private static async Task SetBotCommands()
        {
            var commands = new[]
            {
        new BotCommand { Command = "start", Description = "Начало работы с ботом" },
        new BotCommand { Command = "generate", Description = "Сгенерировать изображение" },
        new BotCommand { Command = "settings", Description = "Настроить параметры генерации" },
        new BotCommand { Command = "info", Description = "Показать текущие настройки" },
            };

            await _BotClient.SetMyCommandsAsync(commands);
        }

        //метод для генерации картинок с помощью stable diffusion (локально на компьютере)
        private static async Task<string> GenerateImage(string prompt)
        {
            // Генерация уникального seed с использованием времени
            long seed = DateTime.UtcNow.Ticks;  // Используем тик времени для уникальности

            // Читаем настройки из файла settings.json
            string settingsFilePath = "settings.json";
            string jsonSettings = File.ReadAllText(settingsFilePath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonSettings);

            // Получаем параметры из настроек с проверками типов
            int width = settings["resolution"].GetString() is string resolutionStr && int.TryParse(resolutionStr, out var tempWidth) ? tempWidth : 1024;
            int height = settings["resolution"].GetString() is string resolutionStrHeight && int.TryParse(resolutionStrHeight, out var tempHeight) ? tempHeight : 1024;
            double guidanceScale = settings["guidance_scale"].TryGetDouble(out var tempGuidanceScale) ? tempGuidanceScale : 7.5;
            int steps = settings["steps"].TryGetInt32(out var tempSteps) ? tempSteps : 50;
            string sampler = settings["sampler"].GetString() ?? "DPM++ 2M Karras"; // Дефолтное значение
            double strength = settings["strength"].TryGetDouble(out var tempStrength) ? tempStrength : 0.75;
            double scale = settings["scale"].TryGetDouble(out var tempScale) ? tempScale : 1.0;

            // Логируем параметры
            Console.WriteLine("Параметры генерации:");
            Console.WriteLine($"Ширина: {width}, Высота: {height}");
            Console.WriteLine($"Guidance Scale: {guidanceScale}, Steps: {steps}");
            Console.WriteLine($"Seed: {seed}, Sampler: {sampler}");
            Console.WriteLine($"Strength: {strength}, Scale: {scale}");

            // Получаем входной текст
            string inputText = settings["input"].GetProperty("text").GetString() ?? "";
            string negativeText = settings["input"].GetProperty("negative_text").GetString() ?? "";

            // Создаем запрос с параметрами
            using (HttpClient client = new HttpClient())
            {
                var requestData = new
                {
                    prompt = prompt ?? inputText, // Если prompt не передан, используем текст из JSON
                    negative_prompt = negativeText,
                    width = width,
                    height = height,
                    steps = steps,
                    guidance_scale = guidanceScale,
                    seed = seed,  // Используем уникальное значение для seed
                    sampler = sampler,
                    strength = strength,
                    scale = scale
                };

                // Логируем сам запрос
                Console.WriteLine("Отправка запроса...");
                Console.WriteLine(JsonSerializer.Serialize(requestData));

                client.Timeout = TimeSpan.FromMinutes(5);
                var response = await client.PostAsJsonAsync("http://127.0.0.1:7860/sdapi/v1/txt2img", requestData);

                if (response.IsSuccessStatusCode)
                {
                    // Логируем успешный ответ
                    var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>();
                    Console.WriteLine("Ответ от API stable diffusion получен!");

                    // Получаем изображение в формате base64
                    string base64Image = jsonResponse.GetProperty("images")[0].GetString();

                    // Сохраняем изображение в файл output.png, перезаписывая старое
                    string filePath = "output.png"; // Путь к файлу для перезаписи
                    File.WriteAllBytes(filePath, Convert.FromBase64String(base64Image));

                    Console.WriteLine($"Изображение сохранено в {filePath}");

                    return filePath; // Возвращаем путь к файлу
                }
                else
                {
                    // Логируем ошибку
                    Console.WriteLine($"Ошибка при генерации изображения. Статус: {response.StatusCode}");
                    return null;
                }
            }
        }

        //метод для обработки в телеграмм боте настроек генерации (загрузка json файла)
        private static async Task HandleGenerateSettingsCommand(Chat chat)
        {
            string settingsFilePath = "settings.json";
            if (!File.Exists(settingsFilePath))
            {
                await _BotClient.SendTextMessageAsync(chat.Id, "Файл настроек не найден.");
                return;
            }

            Console.WriteLine("Чтение настроек из файла...");
            string jsonSettings = File.ReadAllText(settingsFilePath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonSettings);

            // Если файл настроек пуст или не в нужном формате, то выводим ошибку.
            if (settings == null || settings.Count == 0)
            {
                await _BotClient.SendTextMessageAsync(chat.Id, "Не удалось загрузить настройки.");
                return;
            }

            Console.WriteLine($"Чтение настроек завершено, найдено {settings.Count} ключей.");

            var buttons = settings.Keys.Select(key => InlineKeyboardButton.WithCallbackData(key, $"edit_{key}"));
            var keyboard = new InlineKeyboardMarkup(buttons.Select(b => new[] { b }));

            Console.WriteLine("Отправка клавиатуры с настройками...");
            await _BotClient.SendTextMessageAsync(chat.Id, "Выберите параметр для изменения:", replyMarkup: keyboard);
        }

        //метод для обработки в телеграмм боте настроек генерации (если все успешно прошло вводим новоего значений)
        private static async Task HandleEditSetting(Chat chat, string settingKey)
        {
            _pendingSettingsChanges[chat.Id] = settingKey;
            await _BotClient.SendTextMessageAsync(chat.Id, $"Введите новое значение для {settingKey}:");
        }
        
        //метод для сохранения изменений в json
        private static async Task SaveSetting(Chat chat, string newValue)
        {
            if (!_pendingSettingsChanges.ContainsKey(chat.Id)) return;

            string settingKey = _pendingSettingsChanges[chat.Id];
            _pendingSettingsChanges.Remove(chat.Id);

            string settingsFilePath = "settings.json";
            string jsonSettings = File.ReadAllText(settingsFilePath);

            // Убираем символы возврата каретки \r и символы новой строки \n из всего содержимого JSON
            jsonSettings = jsonSettings.Replace("\r", "").Replace("\n", "");

            // Десериализуем JSON в словарь
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonSettings);

            // Обработка вложенных объектов
            if (settingKey.Contains("."))
            {
                // Разделяем ключ по точке для вложенных объектов
                string[] keys = settingKey.Split('.');
                JsonElement temp = settings[keys[0]];

                // Идем по вложенным объектам
                for (int i = 1; i < keys.Length - 1; i++)
                {
                    temp = temp.GetProperty(keys[i]);
                }

                // Изменяем значение
                string lastKey = keys[keys.Length - 1];
                settings[keys[0]] = JsonDocument.Parse($"{{\"{lastKey}\": \"{CleanJsonString(newValue)}\"}}").RootElement;
            }
            else
            {
                // Для обычных настроек
                settings[settingKey] = JsonDocument.Parse($"\"{CleanJsonString(newValue)}\"").RootElement;
            }

            // сохраняем обновленные настройки в файл
            File.WriteAllText(settingsFilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

            // Отправляем сообщение пользователю, что настройка изменена
            await _BotClient.SendTextMessageAsync(chat.Id, $"Настройка `{settingKey}` изменена на `{newValue}`.");
        }

        // Метод для очистки строки от символов, нарушающих формат JSON
        private static string CleanJsonString(string input)
        {
            // Убираем символы возврата каретки и ненужные символы новой строки
            input = input.Replace("\r", "").Replace("\n", "");

            // Экранируем все кавычки в строке
            return input.Replace("\"", "\\\"");
        }

        //метод для информации по настройками генерации
        private static async Task ShowGenerateInfo(Chat chat)
        {
            string settingsFilePath = "settings.json";
            string jsonSettings = File.ReadAllText(settingsFilePath);

            // использования json файла
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonSettings);

            // Формирование текста с настройками
            var settingsInfo = new StringBuilder();
            settingsInfo.AppendLine("Текущие настройки генерации:");

            // Проходим по всем настройкам и происходит вывод
            foreach (var setting in settings)
            {
                settingsInfo.AppendLine($"{setting.Key}: {setting.Value}");
            }

            await _BotClient.SendTextMessageAsync(chat.Id, settingsInfo.ToString());
        }


        private static async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken token)
        {
            try
            {
                if (update.Type == UpdateType.Message)
                    await OnMessage(update);
                else if (update.Type == UpdateType.CallbackQuery)
                    await OnCallBackQuery(update);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в UpdateHandler: {ex.Message}");
            }
        }

        private static async Task OnCallBackQuery(Update update)
        {
            var CallBackQuery = update.CallbackQuery;
            var chat = CallBackQuery.Message.Chat;

            Console.WriteLine($"Получен callback запрос: {CallBackQuery.Data}");

            if (CallBackQuery.Data.StartsWith("edit_"))
            {
                string settingKey = CallBackQuery.Data.Replace("edit_", "");
                await HandleEditSetting(chat, settingKey);
            }
        }


        private static async Task OnMessage(Update update)
        {
            var message = update.Message;
            var user = message.From;
            var chat = message.Chat;

            Console.WriteLine($"Пришло сообщение: {message.Text}, от {user.Username},\nid:{user.Id}");

            if (_pendingSettingsChanges.ContainsKey(chat.Id))
            {
                await SaveSetting(chat, message.Text);
                return;
            }

            // Обработка команды /start
            if (message.Text == "/start")
            {
                string welcomeMessage = "Привет! Я бот для генерации изображений с помощью Stable Diffusion. Вот список доступных команд:\n\n" +
                                        "📌 /generate <текст> — сгенерировать изображение по описанию.\n" +
                                        "⚙️ /generateSettings — настроить параметры генерации.\n" +
                                        "ℹ️ /generateInfo — посмотреть текущие настройки.\n";
                await _BotClient.SendTextMessageAsync(chat.Id, welcomeMessage);
                return;
            }

            // Обработка команды /generateSettings
            if (message.Text == "/generateSettings")
            {
                Console.WriteLine("Команда /generateSettings получена, вызываем HandleGenerateSettingsCommand");
                await HandleGenerateSettingsCommand(chat);
                return;
            }

            // Обработка команды /generateInfo
            if (message.Text == "/generateInfo")
            {
                await ShowGenerateInfo(chat);
                return;
            }

            // Обработка команды /generate
            if (message.Text.StartsWith("/generate"))
            {
                string prompt = message.Text.Replace("/generate", "").Trim();
                await _BotClient.SendTextMessageAsync(chat.Id, string.IsNullOrEmpty(prompt) ? "Пожалуйста, укажите запрос для генерации. Например:\n/generate красивый закат" : "Генерация изображения...");
                if (!string.IsNullOrEmpty(prompt))
                {
                    string imagePath = await GenerateImage(prompt);
                    if (imagePath != null)
                    {
                        using (var stream = File.OpenRead(imagePath))
                        {
                            await _BotClient.SendPhotoAsync(chat.Id, new InputFileStream(stream, "image.png"));
                        }
                    }
                    else
                    {
                        await _BotClient.SendTextMessageAsync(chat.Id, "Не удалось сгенерировать изображение.");
                    }
                }
            }
            else
            {
                await _BotClient.SendTextMessageAsync(chat.Id, "Команда не распознана. Используйте /start для списка команд.");
            }
        }
    }
}
