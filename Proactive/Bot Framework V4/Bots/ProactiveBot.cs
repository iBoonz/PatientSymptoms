// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Schema.Teams;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProactiveBot.Models;
using ProactiveBot.Services;

namespace Microsoft.BotBuilderSamples
{
    public class ProactiveBot : ActivityHandler
    {
        // Message to send to users when the bot receives a Conversation Update event

        private IConfiguration _configuration;

        private IFHIRService _fhirService;

        public ProactiveBot(IConfiguration configuration, IFHIRService fHIRService)
        {
            _configuration = configuration;
            _fhirService = fHIRService;
        }

        private CloudBlobContainer GetCloudBlobContainer(string containerName)
        {
            var storageCredentials = new StorageCredentials(_configuration["StorageName"], _configuration["StorageKey"]);
            var cloudStorageAccount = new CloudStorageAccount(storageCredentials, true);
            var cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();
            var container = cloudBlobClient.GetContainerReference(containerName);
            return container;
        }

        private async Task AddConversationReference(Activity activity)
        {
            var conversationReference = activity.GetConversationReference();
            var metadataContainer = GetCloudBlobContainer("bot-metadata");
            var metadataFile = metadataContainer.GetBlockBlobReference(conversationReference.User.Id);
            var content = JsonConvert.SerializeObject(conversationReference);
            await metadataFile.UploadTextAsync(content);
        }

        private void RemoveConversationReference(Activity activity)
        {
            var conversationReference = activity.GetConversationReference();
            var metadataContainer = GetCloudBlobContainer("bot-metadata");
            var metadataFile = metadataContainer.GetBlockBlobReference(conversationReference.User.Id);
            metadataFile.DeleteIfExists();
        }

        protected override async Task OnConversationUpdateActivityAsync(ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            await base.OnConversationUpdateActivityAsync(turnContext, cancellationToken);
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                // Greet anyone that was not the target (recipient) of this message.
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("Hi! Nice to meet you :)"), cancellationToken);
                }
            }
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                if(turnContext.Activity.Value != null)
                {
                    var activityValueString = turnContext.Activity.Value.ToString();
                    if (activityValueString.Contains("AnatomicalSiteMention"))
                    {
                        var patientSymptomInfo = JsonConvert.DeserializeObject<PatientSymptomInfoDto>(activityValueString);
                        await _fhirService.SendDataToFHIRServer(patientSymptomInfo);
                    }

                    await turnContext.SendActivityAsync(MessageFactory.Text($"The data has been succesfully send to the EMR"), cancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(turnContext.Activity.Text))
                {
                    if (turnContext.Activity.Text.Contains("Unsubscribe"))
                    {
                        RemoveConversationReference(turnContext.Activity as Activity);
                        await turnContext.SendActivityAsync(MessageFactory.Text($"You have been successfully unsubscribed from the patient symptoms"), cancellationToken);
                    }
                    else if (turnContext.Activity.Text.Contains("Subscribe"))
                    {
                        await AddConversationReference(turnContext.Activity as Activity);
                        await turnContext.SendActivityAsync(MessageFactory.Text($"You have been successfully subscribed to the patient symptoms"), cancellationToken);
                    }
                    else
                    {
                        await turnContext.SendActivityAsync(MessageFactory.Text($"Currently I can only let you subscribe or unsubscribe on patient Symptoms"), cancellationToken);
                    }
                }
            }
        }
    }
}
