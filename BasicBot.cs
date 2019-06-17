// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using MySql.Data;
using MySql.Data.MySqlClient;
using System.Data;
//using System.Data;

namespace Microsoft.BotBuilderSamples
{
    
    /// <summary>
    /// Main entry point and orchestration for bot.
    /// </summary>
    public class BasicBot : IBot
    {
        // Supported LUIS Intents
        public const string GreetingIntent = "Greeting";
        public const string CancelIntent = "cancel";
        public const string HelpIntent = "Help";
        public const string NoneIntent = "None";
        public const string FundIntent = "펀드";
        public const string BuyIntent = "buy";
        public const string SearchIntent = "search";
        public const string RecommendIntent = "recommend";
        public static string url = "";


        /// <summary>
        /// Key in the bot config (.bot file) for the LUIS instance.
        /// In the .bot file, multiple instances of LUIS can be configured.
        /// </summary>
        public static readonly string LuisConfiguration = "BasicBotLuisApplication";

        private readonly IStatePropertyAccessor<GreetingState> _greetingStateAccessor;
        private readonly IStatePropertyAccessor<DialogState> _dialogStateAccessor;
        private readonly UserState _userState;
        private readonly ConversationState _conversationState;
        private readonly BotServices _services;

        /// <summary>
        /// Initializes a new instance of the <see cref="BasicBot"/> class.
        /// </summary>
        /// <param name="botServices">Bot services.</param>
        /// <param name="accessors">Bot State Accessors.</param>
        public BasicBot(BotServices services, UserState userState, ConversationState conversationState, ILoggerFactory loggerFactory)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _userState = userState ?? throw new ArgumentNullException(nameof(userState));
            _conversationState = conversationState ?? throw new ArgumentNullException(nameof(conversationState));

            _greetingStateAccessor = _userState.CreateProperty<GreetingState>(nameof(GreetingState));
            _dialogStateAccessor = _conversationState.CreateProperty<DialogState>(nameof(DialogState));

            // Verify LUIS configuration.
            if (!_services.LuisServices.ContainsKey(LuisConfiguration))
            {
                throw new InvalidOperationException($"The bot configuration does not contain a service type of `luis` with the id `{LuisConfiguration}`.");
            }

            Dialogs = new DialogSet(_dialogStateAccessor);
            Dialogs.Add(new GreetingDialog(_greetingStateAccessor, loggerFactory));
        }

        private DialogSet Dialogs { get; set; }

        /// <summary>
        /// Run every turn of the conversation. Handles orchestration of messages.
        /// </summary>
        /// <param name="turnContext">Bot Turn Context.</param>
        /// <param name="cancellationToken">Task CancellationToken.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            var activity = turnContext.Activity;

            // Create a dialog context
            var dc = await Dialogs.CreateContextAsync(turnContext);

            if (activity.Type == ActivityTypes.Message)
            {
                // Perform a call to LUIS to retrieve results for the current activity message.
                var luisResults = await _services.LuisServices[LuisConfiguration].RecognizeAsync(dc.Context, cancellationToken);

                // If any entities were updated, treat as interruption.
                // For example, "no my name is tony" will manifest as an update of the name to be "tony".
                var topScoringIntent = luisResults?.GetTopScoringIntent();

                var topIntent = topScoringIntent.Value.intent;

                // update greeting state with any entities captured
                await UpdateGreetingState(luisResults, dc.Context);

                // Handle conversation interrupts first.
                var interrupted = await IsTurnInterruptedAsync(dc, topIntent);  //여기서 cancel인지 파악
                if (interrupted)
                {
                    // Bypass the dialog.
                    // Save state before the next turn.
                    await _conversationState.SaveChangesAsync(turnContext);
                    await _userState.SaveChangesAsync(turnContext);
                    return;
                }

                // Continue the current dialog
                var dialogResult = await dc.ContinueDialogAsync();

                // if no one has responded,
                if (!dc.Context.Responded)
                {
                    //var entities = luisResults.Entities;
                    bool bFundEntities = CheckFundEntities(luisResults, dc.Context);

                    // examine results from active dialog
                    switch (dialogResult.Status)
                    {
                        case DialogTurnStatus.Empty:
                            switch (topIntent)
                            {
                                case GreetingIntent:
                                    //await dc.BeginDialogAsync(nameof(GreetingDialog));
                                    //break;
                                    var welcomeCard = CreateAdaptiveCardAttachment();
                                    var response11 = CreateResponse(activity, welcomeCard);
                                    await dc.Context.SendActivityAsync(response11);
                                    break;
                                case FundIntent:
                                    {
                                        var response = activity.CreateReply();
                                        var actions = new List<CardAction>();
                                        //actions.Add(new CardAction() { Title = "매수", Value = "http://www.shinhaninvest.com", Type = ActionTypes.OpenUrl});
                                        actions.Add(new CardAction() { Title = "매수", Value = "펀드 매수", Type = ActionTypes.ImBack });
                                        actions.Add(new CardAction() { Title = "매도", Value = "펀드 매도", Type = ActionTypes.ImBack });
                                        actions.Add(new CardAction() { Title = "잔고", Value = "펀드 잔고", Type = ActionTypes.ImBack });
                                        actions.Add(new CardAction() { Title = "검색", Value = "펀드 검색", Type = ActionTypes.ImBack });
                                        response.Attachments.Add(
                                            new HeroCard
                                            {
                                                Title = "원하는 펀드 메뉴를 선택해주세요!",
                                                Buttons = actions,
                                            }.ToAttachment());
                                        await dc.Context.SendActivityAsync(response);
                                    }
                                    break;
                                case BuyIntent:
                                    {
                                        if(bFundEntities == true)
                                        {
                                            var response = activity.CreateReply();
                                            var actions = new List<CardAction>();
                                            actions.Add(new CardAction() { Title = "펀드 매수 화면 웹연결", Value = "https://www.shinhaninvest.com/siw/wealth-management/fund/newBuy/view.do#!", Type = ActionTypes.OpenUrl});
                                            response.Attachments.Add(
                                                new HeroCard
                                                {
                                                    Title = "펀드 매수 화면번호는 1000번입니다.",
                                                    Buttons = actions,
                                                }.ToAttachment());
                                            await dc.Context.SendActivityAsync(response);
                                        }
                                        else
                                            await dc.Context.SendActivityAsync("매수상품을 알려주세요");
                                    }
                                    break;
                                case SearchIntent:
                                    {
                                        SelectUsingAdapter("select * from mapping_table where intent =\"search\"");
                                        if (bFundEntities == true)
                                        {
                                            var response = activity.CreateReply();
                                            var actions = new List<CardAction>();
                                            var askSentence = turnContext.Activity.Text;

                                            //actions.Add(new CardAction() { Title = "펀드 검색 화면으로 가기", Value = "https://www.shinhaninvest.com/siw/wealth-management/fund/search-detail/view.do", Type = ActionTypes.OpenUrl });
                                            actions.Add(new CardAction() { Title = "펀드 검색 화면으로 가기", Value = url, Type = ActionTypes.OpenUrl });
                                            response.Attachments.Add(
                                                new HeroCard
                                                {
                                                    Title = "나에게 딱 어울리는 펀드를 찾아보세요!",
                                                    Buttons = actions,
                                                }.ToAttachment());
                                            await dc.Context.SendActivityAsync(response);
                                        }
                                        else
                                            await dc.Context.SendActivityAsync("어떤걸 검색해 드릴까요 ?");
                                    }
                                    break;
                                case RecommendIntent:
                                    {
                                        if (bFundEntities == true)
                                        {
                                            var response = activity.CreateReply();
                                            var actions = new List<CardAction>();
                                            var askSentence = turnContext.Activity.Text;

                                            actions.Add(new CardAction() { Title = "펀드 추천 화면으로 가기", Value = "https://www.shinhaninvest.com/siw/wealth-management/fund/000101/view.do", Type = ActionTypes.OpenUrl });
                                            response.Attachments.Add(
                                                new HeroCard
                                                {
                                                    Title = "신한금융투자가 추천해드리는 펀드!",
                                                    Buttons = actions,
                                                }.ToAttachment());
                                            await dc.Context.SendActivityAsync(response);
                                        }
                                        else
                                            await dc.Context.SendActivityAsync("어떤걸 추천해 드릴까요 ?");
                                    }
                                    break;
                                case NoneIntent:
                                default:
                                    // Help or no intent identified, either way, let's provide some help.
                                    // to the user
                                    await dc.Context.SendActivityAsync("준비중입니다 :)");
                                    break;
                            }

                            break;
                        case DialogTurnStatus.Waiting:
                            // The active dialog is waiting for a response from the user, so do nothing.
                            break;

                        case DialogTurnStatus.Complete:
                            await dc.EndDialogAsync();

                            break;

                        default:
                            await dc.CancelAllDialogsAsync();
                            break;
                    }
                }
            }
            else if (activity.Type == ActivityTypes.ConversationUpdate)
            {
                if (activity.MembersAdded != null)
                {
                    // Iterate over all new members added to the conversation.
                    foreach (var member in activity.MembersAdded)
                    {
                        // Greet anyone that was not the target (recipient) of this message.
                        // To learn more about Adaptive Cards, see https://aka.ms/msbot-adaptivecards for more details.
                        if (member.Id != activity.Recipient.Id)
                        {
                            var welcomeCard = CreateAdaptiveCardAttachment();
                            var response = CreateResponse(activity, welcomeCard);
                            await dc.Context.SendActivityAsync(response);
                        }
                    }
                }
            }

            await _conversationState.SaveChangesAsync(turnContext);
            await _userState.SaveChangesAsync(turnContext);
        }

        // Determine if an interruption has occurred before we dispatch to any active dialog.
        private async Task<bool> IsTurnInterruptedAsync(DialogContext dc, string topIntent)
        {
            // See if there are any conversation interrupts we need to handle.
            if (topIntent.Equals(CancelIntent))
            {
                if (dc.ActiveDialog != null)
                {
                    await dc.CancelAllDialogsAsync();
                    await dc.Context.SendActivityAsync("Ok. I've canceled our last activity.");
                }
                else
                {
                    await dc.Context.SendActivityAsync("I don't have anything to cancel.");
                }

                return true;        // Handled the interrupt.
            }

            if (topIntent.Equals(HelpIntent))
            {
                await dc.Context.SendActivityAsync("Let me try to provide some help.");
                await dc.Context.SendActivityAsync("I understand greetings, being asked for help, or being asked to cancel what I am doing.");
                if (dc.ActiveDialog != null)
                {
                    await dc.RepromptDialogAsync();
                }

                return true;        // Handled the interrupt.
            }

            return false;           // Did not handle the interrupt.
        }

        // Create an attachment message response.
        private Activity CreateResponse(Activity activity, Attachment attachment)
        {
            var response = activity.CreateReply();
            response.Attachments = new List<Attachment>() { attachment };
            return response;
        }

        // Load attachment from file.
        private Attachment CreateAdaptiveCardAttachment()
        {
            var adaptiveCard = File.ReadAllText(@".\Dialogs\Welcome\Resources\welcomeCard.json");
            return new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCard),
            };
        }

        /// <summary>
        /// Helper function to update greeting state with entities returned by LUIS.
        /// </summary>
        /// <param name="luisResult">LUIS recognizer <see cref="RecognizerResult"/>.</param>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        private async Task UpdateGreetingState(RecognizerResult luisResult, ITurnContext turnContext)
        {
            if (luisResult.Entities != null && luisResult.Entities.HasValues)
            {
                // Get latest GreetingState
                var greetingState = await _greetingStateAccessor.GetAsync(turnContext, () => new GreetingState());
                var entities = luisResult.Entities;

                // Supported LUIS Entities
                string[] userNameEntities = { "userName", "userName_patternAny" };
                string[] userLocationEntities = { "userLocation", "userLocation_patternAny" };

                // Update any entities
                // Note: Consider a confirm dialog, instead of just updating.
                foreach (var name in userNameEntities)
                {
                    // Check if we found valid slot values in entities returned from LUIS.
                    if (entities[name] != null)
                    {
                        // Capitalize and set new user name.
                        var newName = (string)entities[name][0];
                        greetingState.Name = char.ToUpper(newName[0]) + newName.Substring(1);
                        break;
                    }
                }

                foreach (var city in userLocationEntities)
                {
                    if (entities[city] != null)
                    {
                        // Capitalize and set new city.
                        var newCity = (string)entities[city][0];
                        greetingState.City = char.ToUpper(newCity[0]) + newCity.Substring(1);
                        break;
                    }
                }

                // Set the new values into state.
                await _greetingStateAccessor.SetAsync(turnContext, greetingState);
            }
        }

        private bool CheckFundEntities(RecognizerResult luisResult, ITurnContext turnContext)
        {
            var entities = luisResult.Entities;

            // Supported LUIS Entities
            string[] fundEnties = { "상품" };

            foreach (var fund in fundEnties)
            {
                // Check if we found valid slot values in entities returned from LUIS.
                if (entities[fund] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SelectUsingAdapter(string sql)
        {
            DataSet ds = new DataSet();
            string connStr = "Server=db-shinnavi-mysql.mysql.database.azure.com;Database=shinnavi;Uid=admin1@db-shinnavi-mysql;Pwd=a123456#;Charset=utf8";

            using (MySqlConnection conn = new MySqlConnection(connStr))
            {
                //MySqlDataAdapter 클래스를 이용하여
                //비연결 모드로 데이타 가져오기
                //string sql = "SELECT * FROM Tab1 WHERE Id>=2";
                MySqlDataAdapter adpt = new MySqlDataAdapter(sql, conn);
                adpt.Fill(ds, "mapping_table");
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                url = (string)r["url"];               
                //Console.WriteLine(r["intent"]);
            }
        }

    }
    
}
