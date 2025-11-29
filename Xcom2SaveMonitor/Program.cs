using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using ByteSizeLib;

using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

/*
 * Это консольное приложение на C#, которое работает как Telegram-бот. Его основная задача — следить за определённой 
 * папкой на вашем компьютере и отправлять уведомления в Telegram при появлении в ней новых файлов.
 */
namespace Xcom2SaveMonitor
{
    /// <summary>
    /// Программа создана для мониторинга папки с файлами сохранений игры XCOM 2: War of the Chosen. 
    /// Когда игра создаёт новый файл сохранения, бот немедленно отправляет сообщение об этом в Telegram.     
    /// </summary>
    /// <remarks>
    /// Это может быть полезно, например, для отслеживания прогресса в режиме "Терминатор" (Ironman), где каждое сохранение 
    /// критически важно.
    /// </remarks>
    internal class Program
    {
        private const string TelegramBotToken = "8559262822:AAGh8tYuUq0ec6eMM7BcB5OMUvVzZLvzAMY";
        private static readonly string DefaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            @"My Games\XCOM2 War of the Chosen\XComGame\SaveData"
        );
        private static readonly bool _reactOnAutoSaves = false;

        private static readonly HashSet<long> KnownChats = new HashSet<long>();

        private static readonly BotCommand[] SuportedCommands =
        {
            new BotCommand { Command = "start", Description = "Начать диалог с ботом" },
            new BotCommand { Command = "size", Description = "Показать последний файл сохранений" },
            new BotCommand { Command = "size_3", Description = "Показать последние 3 файла сохранений" },
            new BotCommand { Command = "size_6", Description = "Показать последние 6 файлов сохранений" },
            //new BotCommand { Command = "size_10", Description = "Показать последние 10 файлов сохранений" },
        };

        /// <summary>
        /// Может быть задан как параметр командной строки
        /// </summary>
        private static string _directoryToMonitor;
        private static TelegramBotClient _botClient;        

        /// <summary>
        /// •	Для отслеживания изменений в файловой системе используется класс <see cref="FileSystemWatcher"/>.
        /// •	Он настроен так, чтобы реагировать только на создание новых файлов
        ///     (NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size) в указанной папке.
        /// •	Когда создаётся новый файл, срабатывает событие watcher.Created.
        /// •	В обработчике этого события программа:
        /// •	Делает небольшую паузу (await Task.Delay(300)). Это сделано на случай, если файл ещё не полностью записан на диск.
        /// •	Добавляет информацию о новом файле в потокобезопасную очередь LastFiles.Очередь хранит не более 3 последних файлов.
        /// •	Формирует сообщение с именем и размером файла (для удобного отображения размера используется библиотека ByteSizeLib).
        /// •	Отправляет это сообщение всем пользователям, которые когда-либо писали боту.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args)
        {
            _directoryToMonitor = args.Length > 0 ? args[0] : DefaultDirectory;
            if (!Directory.Exists(_directoryToMonitor))
            {
                Console.WriteLine("Каталог не найден: " + _directoryToMonitor);
                return;
            }

            _botClient = new TelegramBotClient(TelegramBotToken);

            var cts = new CancellationTokenSource();

            await SetBotCommandsAsync(_botClient, cts.Token, SuportedCommands);

            // Запуск получения апдейтов через UpdateHandler
            _botClient.StartReceiving(
                updateHandler: new UpdateHandler(_botClient),
                receiverOptions: new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
                cancellationToken: cts.Token
            );

            Console.WriteLine("Запущен мониторинг каталога: " + _directoryToMonitor);

            StartDirectoryMonitor(_botClient, _directoryToMonitor);

            Console.WriteLine("Для завершения работы нажмите Ctrl+C.");
            await Task.Delay(Timeout.Infinite);
        }



        /// <summary>
        /// Метод отслеживания файлов в Директории:
        /// •	Для отслеживания изменений в файловой системе используется класс <see cref="FileSystemWatcher"/>.
        /// •	Он настроен так, чтобы реагировать только на <strong>создание</strong> новых файлов
        ///     (NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size) в указанной папке.
        /// •	Когда создаётся новый файл, срабатывает событие watcher.Created.
        /// •	В обработчике этого события программа:
        /// •	Делает небольшую паузу (await Task.Delay(300)). Это сделано на случай, если файл ещё не полностью записан на диск.
        /// •	Добавляет информацию о новом файле в потокобезопасную очередь LastFiles. Очередь хранит не более 3 последних файлов.
        /// •	Формирует сообщение с именем и размером файла (для удобного отображения размера используется библиотека ByteSizeLib).
        /// •	Отправляет это сообщение всем пользователям, которые когда-либо писали боту.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private static void StartDirectoryMonitor(TelegramBotClient botClient, string directory)
        {
            var watcher = new FileSystemWatcher(directory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            watcher.Renamed += Watcher_Created;
            watcher.Created += Watcher_Created;
        }

        private static async void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            try
            {
                await Task.Delay(500);
                if ((Path.GetExtension(e.Name).Trim() != string.Empty) ||
                    (!_reactOnAutoSaves && e.Name.Contains("АВТОСОХРАНЕНИЕ")))
                {
                    return;
                }
                var fileInfo = new FileInfo(e.FullPath);

                if (fileInfo.DirectoryName == Program._directoryToMonitor && fileInfo.Exists)
                {
                    var formatedSize = ByteSize.FromBytes(fileInfo.Length);
                    var msg = $"ВНИМАНИЕ:       [ {formatedSize} ]\n\n " +
                        $"<i>\"{fileInfo.Name}\"</i>\n\n" +
                        $"Размер:    <b>{formatedSize}</b>\n\n" +
                        $"<code>Дата создания {fileInfo.CreationTime}</code>";
                    await SendMessageToAllChats(_botClient, msg).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await SendMessageToAllChats(_botClient, "Ошибка при обработке нового файла: " + ex.Message)
                    .ConfigureAwait(false);
            }            
        }

        private static async Task SendMessageToAllChats(ITelegramBotClient botClient, string message)
        {
            foreach (var chatId in KnownChats)
            {
                try
                {
                    await botClient.SendMessage(
                        chatId: chatId,
                        text: message,
                        parseMode: ParseMode.Html
                    ).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка отправки сообщения в чат {chatId}: {ex.Message}");
                }
            }
        }


        private static Task SetBotCommandsAsync(ITelegramBotClient botClient, CancellationToken cancellationToken, 
            params BotCommand[] commands)
        {
            if (commands == null || commands.Length == 0)
            {
                return Task.FromException(new NullReferenceException($"Параметр {nameof(commands)} НЕ может быть пустым!"));
            }
            // Используем SuportedCommands вместо несуществующего AppCommands
            // Метод SetMyCommandsAsync доступен у TelegramBotClient, а не у интерфейса ITelegramBotClient.
            // Поэтому приводим botClient к TelegramBotClient, если это возможно.
            if (botClient is TelegramBotClient concreteClient)
            {
                return botClient.SetMyCommands(SuportedCommands, cancellationToken: cancellationToken);
            }
            // Если приведение невозможно, возвращаем завершённую задачу.
            return Task.CompletedTask;
        }



        // Реализация обработчика апдейтов через интерфейс IUpdateHandler
        public class UpdateHandler : IUpdateHandler
        {
            private readonly ITelegramBotClient botClient;

            public UpdateHandler(ITelegramBotClient botClient)
            {
                this.botClient = botClient;
            }

            public Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
            {
                try
                {
                    if (update.Type != UpdateType.Message || update.Message?.Text == null)
                    {
                        return Task.CompletedTask;
                    }
                    var chatId = update.Message.Chat.Id;
                    KnownChats.Add(chatId);

                    var text = update.Message.Text.Trim();

                    bool isSizeCommand = false;
                    BotCommand foundCommand = null;
                    if (update.Message.Entities != null)
                    {
                        foreach (var entity in update.Message.Entities)
                        {
                            if (entity.Type == MessageEntityType.BotCommand)
                            {
                                var command = text.Substring(entity.Offset, entity.Length).TrimStart('/');
                                foundCommand = SuportedCommands.FirstOrDefault(x =>
                                    x.Command.Equals(command, StringComparison.InvariantCultureIgnoreCase));
                                if (foundCommand != default)
                                {
                                    isSizeCommand = true;
                                    break;
                                }
                            }
                        }
                    }
                    // Принимаем любые команды вроде size {число}
                    if (!isSizeCommand && Regex.IsMatch(text, @"^\s*[/\\]?size[\s-_=]?\d+\s*$"))
                    {
                        isSizeCommand = true;
                    }

                    if (isSizeCommand)
                    {
                        var cmd = foundCommand?.Command ?? text;
                        int requestedFilesCount = Regex.Match(cmd, @"\d+").Value.ToInt() ?? 1;
                        if (requestedFilesCount == 0)                        
                        {
                            return botClient.SendMessage(
                                chatId: chatId,
                                text: "Вы запросили 0 файлов!",
                                cancellationToken: cancellationToken
                            );
                        }
                        else
                        {
                            var sb = new StringBuilder($"Последние {requestedFilesCount} файлов:\n\n", requestedFilesCount);                            
                            var lastNfiles = new DirectoryInfo(Program._directoryToMonitor)
                                .EnumerateFiles()
                                .OrderByDescending(f => f.CreationTime)
                                .Take(requestedFilesCount)
                                .ToList();
                            int i = 0;
                            foreach (var x in lastNfiles)
                            {
                                sb.AppendLine($"{++i}. <i>\"{x.Name}\"</i>\n" +
                                    $"Размер: <b>{ByteSize.FromBytes(x.Length)}</b>\n" +
                                    $"<code>Дата создания {x.CreationTime}</code>\n");
                            }
                            return botClient.SendMessage(
                                chatId: chatId,
                                text: sb.ToString(),
                                parseMode: ParseMode.Html,
                                cancellationToken: cancellationToken
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка в обработчике апдейтов: {ex}");
                }
                return Task.CompletedTask;
            }

            public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
            {
                Console.WriteLine($"Ошибка Telegram: {exception}");
                return Task.CompletedTask;
            }

        }
    }
}