using System.Text.Json;
using StockAnalyzer.Data;
using StockAnalyzer.Models;

class Program
{
    static readonly HttpClient httpClient = new HttpClient
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" },
            { "Accept", "application/json" }
        }
    };

    static readonly SemaphoreSlim semaphore = new SemaphoreSlim(5);
    static readonly DatabaseHelper dbHelper = new DatabaseHelper();

    static async Task Main()
    {
        try
        {
            Console.WriteLine(" Анализатор акций ");
            Console.WriteLine("Введите тикеры через запятую или пробел (например: AAPL, MSFT, GOOGL):");
            
            string input = Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("Ошибка: Не введены тикеры.");
                return;
            }
            
            string[] tickers = input.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (tickers.Length == 0)
            {
                Console.WriteLine("Ошибка: Не распознаны тикеры.");
                return;
            }
            
            for (int i = 0; i < tickers.Length; i++) //удаление пробелов 
            {
                tickers[i] = tickers[i].Trim().ToUpper();
            }

            Console.WriteLine($"\nОбработка тикеров: {string.Join(", ", tickers)}");

            List<Task> tasks = new List<Task>();

            foreach (var ticker in tickers)
            {
                tasks.Add(ProcessTickerAsync(ticker));
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Критическая ошибка: {ex.Message}");
        }
        
        Console.WriteLine("\nНажмите любую клавишу для выхода...");
        Console.ReadKey();
    }

    static async Task ProcessTickerAsync(string symbol)
    {
        await semaphore.WaitAsync();

        try
        {
            long end = DateTimeOffset.UtcNow.ToUnixTimeSeconds();  // вычисление временных меток для запроса 
            long start = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(); 

            string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}" +
                         $"?period1={start}&period2={end}&interval=1d";

            HttpResponseMessage response = await httpClient.GetAsync(url); //выполнение запроса 
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("chart", out var chart) || !chart.TryGetProperty("result", out var resultArr)) //проверка структуры файла 
            {
                Console.WriteLine($"Ошибка: В отвее отсутствует секция 'chart' или 'result' для {symbol}.");
                return;
            }

            if (resultArr.ValueKind != JsonValueKind.Array || resultArr.GetArrayLength() == 0) //проверка что он ен пустой 
            {
                Console.WriteLine($"Нет данных или некорректный ответ для {symbol}.");
                return;
            }

            var result = resultArr[0];
            var timestamps = result.GetProperty("timestamp");
            var indicators = result.GetProperty("indicators");

            if (!indicators.TryGetProperty("quote", out var quoteArr) || quoteArr.GetArrayLength() == 0)
            {
                Console.WriteLine($"Нет данных 'quote' для {symbol}.");
                return;
            }

            var closePrices = quoteArr[0].GetProperty("close");
            
            int tickerId = dbHelper.GetOrCreateTicker(symbol);
            
            for (int i = 0; i < timestamps.GetArrayLength(); i++)
            {
                if (closePrices[i].ValueKind != JsonValueKind.Number) continue;

                long unixTimestamp = timestamps[i].GetInt64();
                DateTime date = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).Date;
                decimal priceValue = (decimal)closePrices[i].GetDouble();
                
                if (!dbHelper.PriceExists(tickerId, date))
                {
                    dbHelper.InsertPrice(tickerId, date, priceValue);
                }
            }
            
            var lastTwoPrices = dbHelper.GetLastTwoPrices(tickerId);

            if (lastTwoPrices.Count < 2)
            {
                Console.WriteLine($"{symbol} — мало данных для сравнения.");
                return;
            }

            var todayPrice = lastTwoPrices[0];
            var yesterdayPrice = lastTwoPrices[1];

            string state = (todayPrice.Value > yesterdayPrice.Value) ? "Выросла" : "Упала";
            
            dbHelper.UpdateTodaysCondition(tickerId, state);

            Console.WriteLine($" {symbol} — Готово. Состояние: {state}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($" Ошибка сети для {symbol}: {ex.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Ошибка обработки {symbol}: {ex.Message}");
        }
        finally
        {
            semaphore.Release();
        }
    }
}
