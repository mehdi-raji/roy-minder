﻿using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;
using YadetNare.Domain.Activity;
using YadetNare.Shared;

namespace YadetNare.Domain.UpdateHandler;

public class UpdateHandler(ITelegramBotClient bot, IActivityService activityService, ILogger<UpdateHandler> logger)
    : IUpdateHandler
{
    private static readonly InputPollOption[] PollOptions = ["Hello", "World!"];

    public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("HandleError: {Exception}", exception);
        // Cooldown in case of network connection error
        if (exception is RequestException)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await (update switch
        {
            { Message: { } message } => OnMessage(message),
            { EditedMessage: { } message } => OnMessage(message),
            { CallbackQuery: { } callbackQuery } => OnCallbackQuery(callbackQuery),
            { InlineQuery: { } inlineQuery } => OnInlineQuery(inlineQuery),
            { ChosenInlineResult: { } chosenInlineResult } => OnChosenInlineResult(chosenInlineResult),
            { Poll: { } poll } => OnPoll(poll),
            { PollAnswer: { } pollAnswer } => OnPollAnswer(pollAnswer),
            // UpdateType.ChannelPost:
            // UpdateType.EditedChannelPost:
            // UpdateType.ShippingQuery:
            // UpdateType.PreCheckoutQuery:
            _ => UnknownUpdateHandlerAsync(update)
        });
    }

    private async Task OnMessage(Message msg)
    {
        logger.LogInformation("Receive message type: {MessageType}", msg.Type);
        if (msg.Text is not { } messageText)
            return;

        await (messageText switch
        {
            "/inline_buttons" => InlineKeyboard(msg),
            "/keyboard" => SendUsageReplyKeyboard(msg),
            "/remove" => RemoveKeyboard(msg),
            "/request" => RequestContactAndLocation(msg),
            "/inline_mode" => StartInlineQuery(msg),
            "/throw" => FailingHandler(msg),
            $"{Text.DontForget}" => ShowActivities(msg),
            $"{Text.AddActivity}" => ShowActivities(msg),
            _ => Default(msg)
        });
    }

    private async Task ShowActivities(Message msg)
    {
        var inlineMarkup = new InlineKeyboardMarkup();

        var activities = await activityService.GetAll(msg.Chat.Id);
        inlineMarkup = activities.Aggregate(inlineMarkup,
            (current, activity) => current.AddButton($"{Emoji.Pushpin} {activity.Title}", CallBackQueryHelper.GenerateShowData<YadetNare.Entity.User.Activity>(activity.Id)));
        inlineMarkup.AddNewRow()
            .AddButton(Text.AddActivity, nameof(Text.AddActivity));
        
        await bot.SendMessage(msg.Chat, "یادت نره!", replyMarkup: inlineMarkup);
        
        
    }

    private async Task Default(Message msg)
    {
        await Usage(msg);
        await SendUsageReplyKeyboard(msg);
    }

    async Task Usage(Message msg)
    {
        const string usage = """
                                 <b><u>Bot menu</u></b>:
                                 /inline_buttons - send inline buttons
                                 /keyboard       - send keyboard buttons
                                 /remove         - remove keyboard buttons
                                 /request        - request location or contact
                                 /inline_mode    - send inline-mode results list
                                 /throw          - what happens if handler fails
                             """;
        await bot.SendMessage(msg.Chat, usage, parseMode: ParseMode.Html, replyMarkup: new ReplyKeyboardRemove());
    }

    // Send inline keyboard. You can process responses in OnCallbackQuery handler
    private async Task InlineKeyboard(Message msg)
    {
        var inlineMarkup = new InlineKeyboardMarkup()
            .AddNewRow("1.1", "1.2", "1.3")
            .AddNewRow()
            .AddButton("WithCallbackData", "CallbackData")
            .AddButton(InlineKeyboardButton.WithUrl("WithUrl", "https://github.com/TelegramBots/Telegram.Bot"));
        await bot.SendMessage(msg.Chat, "Inline buttons:", replyMarkup: inlineMarkup);
    }

    private async Task SendUsageReplyKeyboard(Message msg)
    {
        var replyMarkup = new ReplyKeyboardMarkup(true)
            .AddNewRow(Text.DontForget)
            .AddNewRow().AddButton("2.1").AddButton("2.2");
        
        await bot.SendMessage(msg.Chat, "Keyboard buttons:", replyMarkup: replyMarkup);
    }

    private async Task RemoveKeyboard(Message msg)
    {
        await bot.SendMessage(msg.Chat, "Removing keyboard", replyMarkup: new ReplyKeyboardRemove());
    }

    private async Task RequestContactAndLocation(Message msg)
    {
        var replyMarkup = new ReplyKeyboardMarkup(true)
            .AddButton(KeyboardButton.WithRequestLocation("Location"))
            .AddButton(KeyboardButton.WithRequestContact("Contact"));
        await bot.SendMessage(msg.Chat, "Who or Where are you?", replyMarkup: replyMarkup);
    }

    private async Task StartInlineQuery(Message msg)
    {
        var button = InlineKeyboardButton.WithSwitchInlineQueryCurrentChat("Inline Mode");
        await bot.SendMessage(msg.Chat, "Press the button to start Inline Query\n\n" +
                                        "(Make sure you enabled Inline Mode in @BotFather)",
            replyMarkup: new InlineKeyboardMarkup(button));
    }

    private static Task FailingHandler(Message msg)
    {
        throw new NotImplementedException("FailingHandler");
    }

    // Process Inline Keyboard callback data
    private async Task OnCallbackQuery(CallbackQuery callbackQuery)
    {
        var data = callbackQuery.Data;
        await (data switch
        {
            nameof(Text.AddActivity) => AddActivity(callbackQuery),
            
            _ => Default(callbackQuery.Message)
        });
        
        logger.LogInformation("Received inline keyboard callback from: {CallbackQueryId}", callbackQuery.Id);
        await bot.AnswerCallbackQuery(callbackQuery.Id, $"Received {callbackQuery.Data}");
        await bot.SendMessage(callbackQuery.Message!.Chat, $"Received {callbackQuery.Data}");
    }

    private async Task AddActivity(CallbackQuery callbackQuery)
    {
        
    }
    #region Inline Mode

    private async Task OnInlineQuery(InlineQuery inlineQuery)
    {
        logger.LogInformation("Received inline query from: {InlineQueryFromId}", inlineQuery.From.Id);

        InlineQueryResult[] results =
        [ // displayed result
            new InlineQueryResultArticle("1", "Telegram.Bot", new InputTextMessageContent("hello")),
            new InlineQueryResultArticle("2", "is the best", new InputTextMessageContent("world"))
        ];
        await bot.AnswerInlineQuery(inlineQuery.Id, results, cacheTime: 0, isPersonal: true);
    }

    private async Task OnChosenInlineResult(ChosenInlineResult chosenInlineResult)
    {
        logger.LogInformation("Received inline result: {ChosenInlineResultId}", chosenInlineResult.ResultId);
        await bot.SendMessage(chosenInlineResult.From.Id, $"You chose result with Id: {chosenInlineResult.ResultId}");
    }

    #endregion

    private Task OnPoll(Poll poll)
    {
        logger.LogInformation("Received Poll info: {Question}", poll.Question);
        return Task.CompletedTask;
    }

    private async Task OnPollAnswer(PollAnswer pollAnswer)
    {
        var answer = pollAnswer.OptionIds.FirstOrDefault();
        var selectedOption = PollOptions[answer];
        if (pollAnswer.User != null)
            await bot.SendMessage(pollAnswer.User.Id, $"You've chosen: {selectedOption.Text} in poll");
    }

    private Task UnknownUpdateHandlerAsync(Update update)
    {
        logger.LogInformation("Unknown update type: {UpdateType}", update.Type);
        return Task.CompletedTask;
    }
}