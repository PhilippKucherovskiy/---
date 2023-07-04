using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class Bot : BackgroundService
{
    private ITelegramBotClient _telegramClient;
    private int _lastUpdateId = 0;

    public Bot(ITelegramBotClient telegramClient)
    {
        _telegramClient = telegramClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _telegramClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions() { AllowedUpdates = { } }, // Разрешенные обновления
            cancellationToken: stoppingToken);

        Console.WriteLine("Бот запущен");

        await SendPoemPeriodicallyAsync(stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        // Является ли сообщение текстовым и содержит ли оно команду "/start"
        if (update.Message.Type == MessageType.Text && update.Message.Text == "/start")
        {
            // Отправляем приветственное сообщение 
            await botClient.SendTextMessageAsync(update.Message.Chat.Id, "Привет! Я буду присылать стихотворения о Денисе каждые 10 минут.");
        }
    }

    private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        // Сообщение об ошибке в зависимости от того, какая именно ошибка произошла
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        // Вывожу в консоль информацию об ошибке
        Console.WriteLine(errorMessage);

        // Задержка перед повторным подключением
        Console.WriteLine("Ожидаем 10 секунд перед повторным подключением.");
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
    }

    private async Task SendPoemPeriodicallyAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Генерируем стихотворение
                string poem = await GeneratePoemAsync();

                // Отправляем стихотворение пользователям
                await SendPoemToUsers(poem);

                // Ожидаем 10 минут перед отправкой следующего стихотворения
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке стихотворения: {ex.Message}");
            }
        }
    }

    public class OpenAIApiResponse
    {
        public List<OpenAIApiChoice> Choices { get; set; }
    }

    public class OpenAIApiChoice
    {
        public string Text { get; set; }
    }

    private async Task<string> GeneratePoemAsync()
    {
        string apiUrl = "https://api.openai.com/v1/engines/gpt-3.5-turbo-16k-0613/completions";
        string apiKey = "API-ключ";

        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var requestData = new
            {
                prompt = "Моего друга Дениса посадили в тюрьму незаконно, потому что он пинал мусорки, хулиганил и насолил президенту Беларуси. Но человек хороший. Сгенерируй стихотворение про него.",
                max_tokens = 50
            };

            var response = await client.PostAsJsonAsync(apiUrl, requestData);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine("Ответ от API:");
            Console.WriteLine(responseContent);

            var apiResponse = JsonConvert.DeserializeObject<OpenAIApiResponse>(responseContent);

            if (apiResponse != null && apiResponse.Choices != null && apiResponse.Choices.Count > 0)
            {
                string generatedPoem = apiResponse.Choices[0].Text;
                return generatedPoem;
            }
            else
            {
                throw new Exception("Не удалось получить стихотворение от API.");
            }
        }
    }

    public async Task SendPoemToUsers(string poem)
    {
        var updates = await _telegramClient.GetUpdatesAsync(offset: _lastUpdateId + 1);

        foreach (var update in updates)
        {
            if (update.Message != null)
            {
                await _telegramClient.SendTextMessageAsync(update.Message.Chat.Id, poem);
            }
            _lastUpdateId = update.Id;
        }
    }
}
