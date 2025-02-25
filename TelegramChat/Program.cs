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


namespace TelegramChat
{
    internal class Program
    {
        private static TelegramBotClient _BotClient;
        static async Task Main(string[] args)
        {
            string Token = ConfigurationManager.ConnectionStrings["TelegramBotToken"].ConnectionString;
            _BotClient = new(Token);

            var me = await _BotClient.GetMe();
            var ResiveOption = new ReceiverOptions
            {
                AllowedUpdates = new[] {UpdateType.Message, UpdateType.CallbackQuery,},
                DropPendingUpdates = true
            };
            var cts = new CancellationTokenSource();

            _BotClient.StartReceiving(UpdateHandler,ErrorHandler, ResiveOption, cts.Token);

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
        //добавлена генерация изображения stable diffusion
        private static async Task<string> GenerateImage(string prompt)
        {
            using (HttpClient client = new HttpClient())
            {
                var requestData = new
                {
                    prompt = prompt,
                    steps = 20,  // Количество шагов генерации
                    width = 1024,
                    height = 1024
                };

                var response = await client.PostAsJsonAsync("http://127.0.0.1:7860/sdapi/v1/txt2img", requestData);
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>();
                    string base64Image = jsonResponse.GetProperty("images")[0].GetString();

                    // Сохраняем изображение в файл
                    string filePath = "output.png";
                    File.WriteAllBytes(filePath, Convert.FromBase64String(base64Image));

                    return filePath; // Возвращаем путь к файлу
                }
                else
                {
                    return null;
                }
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

        private static async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken token)
        {
            try
            {
                switch (update.Type)
                {
                    case UpdateType.Message:
                        OnMessage(update);
                        break;

                    case UpdateType.CallbackQuery:
                        OnCallBackQuery(update);
                        break;

                    default:
                        Console.WriteLine("Введен некоректный запрос!");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Program.UpdateHandler(Exeption): {ex.Message}");
            }
        }

        private static async Task OnCallBackQuery(Update update)
        {
            var CallBackQuery = update.CallbackQuery;
            var user = CallBackQuery.From;
            var chat = CallBackQuery.Message.Chat;

            Console.WriteLine($"{user.Username}, нажал на кнопку: {CallBackQuery.Data}");

            switch (CallBackQuery.Data)
            {
                case "Button 1":
                    await _BotClient.AnswerCallbackQueryAsync(CallBackQuery.Id);
                    await _BotClient.SendMessage
                        (
                        chat.Id,
                        $"Вы нажали на кнопку: {CallBackQuery.Data}"
                        );
                    break;
                case "Button 2":
                    await _BotClient.AnswerCallbackQueryAsync(CallBackQuery.Id);
                    await _BotClient.SendMessage
                        (
                        chat.Id,
                        $"Вы нажали на кнопку: {CallBackQuery.Data}"
                        );
                    break;
                case "Button 3":
                    await _BotClient.AnswerCallbackQueryAsync(CallBackQuery.Id);
                    await _BotClient.SendMessage
                        (
                        chat.Id,
                        $"Вы нажали на кнопку: {CallBackQuery.Data}"
                        );
                    break;
            }
        }

        private static async Task OnMessage(Update update)
        {
            var message = update.Message;
            var user = update.Message.From;
            var chat = message.Chat;

            Console.WriteLine($"Пришло сообщение: {message.Text}, от {user.Username},\nid:{user.Id}");

            if (message.Text == "привет")
            {
                await _BotClient.SendMessage(
                chat.Id,
                $"Привет, {user.Username}",
                replyParameters: message.MessageId
                );
            }
            else if (message.Text.ToLower() == "пока")
            {
                await _BotClient.SendMessage(
                chat.Id,
                $"Пока, {user.Username}",
                replyParameters: message.MessageId
                );
            }
            else if (message.Text == "/start")
            {
                await _BotClient.SendMessage(
                chat.Id,
                "Выберите тип клавиатуры:\n" +
                "/inline\n" +
                "/reply"
                );
            }
            else if (message.Text == "/inline")
            {
                var inlineKeyboard = new InlineKeyboardMarkup(
                    new List<InlineKeyboardButton[]>()
                    {
                        new InlineKeyboardButton[]
                        {
                            InlineKeyboardButton.WithUrl("Cыллка на сайт", "https://metahid.com")
                        },
                        new InlineKeyboardButton[]
                        {
                            InlineKeyboardButton.WithCallbackData("Кнопка #1","Button 1"),
                            InlineKeyboardButton.WithCallbackData("Кнопка #2","Button 2"),
                            InlineKeyboardButton.WithCallbackData("Кнопка #3","Button 3"),
                        }
                    }
                    );

                await _BotClient.SendMessage(
                chat.Id,
                "Выбрана клавиатура Inline!",
                replyMarkup: inlineKeyboard
                );
            }
            else if (message.Text == "/reply")
            {
                var replyKeyboard = new ReplyKeyboardMarkup
                 (
                    new List<KeyboardButton[]>()
                    {
                        new KeyboardButton[]
                        {
                            new KeyboardButton("привет"),
                            new KeyboardButton("пока")
                        },
                       new KeyboardButton[]
                        {
                            new KeyboardButton("еще одна кнопка")
                        }
                    }
                 )
                {
                    ResizeKeyboard = true,
                };

                await _BotClient.SendMessage(
                chat.Id,
                "Выбрана клавиатура reply!",
                replyMarkup: replyKeyboard
                );
            }
            //добавлена команда для генерации изображения /generate cats in boots
            else if (message.Text.StartsWith("/generate"))
            {
                string prompt = message.Text.Replace("/generate", "").Trim();

                if (string.IsNullOrEmpty(prompt))
                {
                    await _BotClient.SendTextMessageAsync(chat.Id, "Пожалуйста, укажите запрос для генерации. Например:\n/generate красивый закат");
                }
                else
                {
                    await _BotClient.SendTextMessageAsync(chat.Id, "Генерация изображения...");

                    string imagePath = await GenerateImage(prompt);
                    if (imagePath != null)
                    {
                        using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
                        {
                            await _BotClient.SendPhotoAsync(chat.Id, new InputFileStream(stream, "image.png"));
                        }
                    }
                    else
                    {
                        await _BotClient.SendTextMessageAsync(chat.Id, "Ошибка генерации изображения.");
                    }
                }
            }
            else
            {
                await _BotClient.SendMessage(
                chat.Id,
                $"Команда не распознанa"
                );
            }

        }
    }
}
    

