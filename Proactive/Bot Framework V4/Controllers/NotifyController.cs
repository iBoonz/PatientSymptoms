// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdaptiveCards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.BotFramework;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Schema.Teams;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProactiveBot.Models;
using ProactiveBot.Services;

namespace ProactiveBot.Controllers
{
    [Route("api/notify")]
    [ApiController]
    public class NotifyController : ControllerBase
    {
        private readonly IBotFrameworkHttpAdapter _adapter;
        private readonly string _appId;
        private readonly SimpleCredentialProvider _credentialProvider;
        private readonly IConfiguration _configuration;

        public NotifyController(IBotFrameworkHttpAdapter adapter, IConfiguration configuration, 
            ICredentialProvider CredentialProvider)
        {
            _adapter = adapter;
            _appId = configuration["MicrosoftAppId"];
            _configuration = configuration;
            _credentialProvider = CredentialProvider as ConfigurationCredentialProvider;

            // If the channel is the Emulator, and authentication is not in use,
            // the AppId will be null.  We generate a random AppId for this case only.
            // This is not required for production, since the AppId will have a value.
            if (string.IsNullOrEmpty(_appId))
            {
                _appId = Guid.NewGuid().ToString(); //if no AppId, use a random Guid
            }
        }

        private CloudBlobContainer GetCloudBlobContainer(string containerName)
        {
            var storageCredentials = new StorageCredentials(_configuration["StorageName"], _configuration["StorageKey"]);
            var cloudStorageAccount = new CloudStorageAccount(storageCredentials, true);
            var cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();
            var container = cloudBlobClient.GetContainerReference(containerName);
            return container;
        }

        [HttpPost("HealthBot")]
        public async Task<IActionResult> PostToHealthBot([FromBody]PatientSymptomInfoDto patientSymptomInfo)
        {
            if (string.IsNullOrWhiteSpace(patientSymptomInfo.Key) || patientSymptomInfo.Key != "tRwMs2Rw0U4si5fNZve3GZU6vskxCpfYLPFog")
            {
                return BadRequest();
            }

            var message = System.IO.File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeploymentTemplates/healthbot-proactive-message.json"));
            var healthBotProactiveMessage = JsonConvert.DeserializeObject<HealthBotProActiveMessage>(message);
            healthBotProactiveMessage.args = new Args
            {
                Doctor = patientSymptomInfo.Doctor,
                AnatomicalSiteMention = patientSymptomInfo.AnatomicalSiteMention,
                DiseaseDisorderMention = patientSymptomInfo.DiseaseDisorderMention,
                Identifier = patientSymptomInfo.Identifier,
                MedicationMention = patientSymptomInfo.MedicationMention,
                PatientDob = patientSymptomInfo.PatientDob,
                PatientName = patientSymptomInfo.PatientName,
                SignSymptomMention = patientSymptomInfo.SignSymptomMention,
                Symptoms = patientSymptomInfo.Symptoms
            };

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken());
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var httpContent = new StringContent(JsonConvert.SerializeObject(healthBotProactiveMessage), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync("https://bot-eu.healthbot.microsoft.com/api/tenants/beloning-9my8uj9/beginScenario", httpContent);
            }

            return Ok();
        }

        private string GenerateToken(int expireMinutes = 200)
        {
            var symmetricKey = Encoding.UTF8.GetBytes(_configuration["API_JWT_secret"]);
            var tokenHandler = new JwtSecurityTokenHandler();

            var now = DateTime.UtcNow;
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                        {
                         new Claim("tenantName", _configuration["HealthBotTenant"]),
                         new Claim("iat", DateTimeOffset.Now.AddMinutes(-1).ToUnixTimeSeconds().ToString()),
                    }),

                Expires = now.AddMinutes(Convert.ToInt32(expireMinutes)),

                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(symmetricKey), SecurityAlgorithms.HmacSha256Signature)
            };

            var stoken = tokenHandler.CreateToken(tokenDescriptor);
            var token = tokenHandler.WriteToken(stoken);

            return token;
        }


        [HttpPost]
        public async Task<IActionResult> Post([FromBody]PatientSymptomInfoDto patientSymptomInfo)
        {
            if(string.IsNullOrWhiteSpace(patientSymptomInfo.Key) || patientSymptomInfo.Key != "s2Rw0......")
            {
                return new ContentResult()
                {
                    Content = "<html><body><h1>Not all required fields are provided.</h1></body></html>",
                    ContentType = "text/html",
                    StatusCode = (int)HttpStatusCode.Unauthorized,
                };
            }
            var container = GetCloudBlobContainer("bot-metadata");
            BlobContinuationToken continuationToken = null;
            int? maxResultsPerQuery = null;
            var response = await container.ListBlobsSegmentedAsync(string.Empty, true, BlobListingDetails.Metadata, maxResultsPerQuery, continuationToken, null, null);
            continuationToken = response.ContinuationToken;
            foreach (var item in response.Results.OfType<CloudBlockBlob>())
            {
                using (MemoryStream mem = new MemoryStream())
                {
                    await item.DownloadToStreamAsync(mem);
                    mem.Position = 0;
                    StreamReader reader = new StreamReader(mem);
                    var conversationReference = JsonConvert.DeserializeObject<ConversationReference>(reader.ReadToEnd());
                    await ((BotAdapter)_adapter).ContinueConversationAsync(_appId, conversationReference, async (turnContext, token) => {
                        MicrosoftAppCredentials.TrustServiceUrl(turnContext.Activity.ServiceUrl);
                        var connectorClient = new ConnectorClient(new Uri(turnContext.Activity.ServiceUrl), _credentialProvider.AppId, _credentialProvider.Password);

                        var parameters = new ConversationParameters
                        {
                            Bot = turnContext.Activity.Recipient,
                            Members = new List<ChannelAccount> { turnContext.Activity.From },
                            ChannelData = JObject.FromObject(
                                new TeamsChannelData
                                {

                                    Tenant = new TenantInfo
                                    {
                                        Id = "YOUR TENANT ID",
                                    },
                                    Channel = new ChannelInfo
                                    {
                                        Id = "YOUR CHANNEL ID@thread.skype"
                                    }
                                },
                                JsonSerializer.Create(new JsonSerializerSettings()
                                {
                                    NullValueHandling = NullValueHandling.Ignore,
                                })),
                        };

                        var conversationResource = await connectorClient.Conversations.CreateConversationAsync(parameters);
                        var message = Activity.CreateMessageActivity();

                        var adaptiveCard = new AdaptiveCard(new AdaptiveSchemaVersion(1, 0))
                        {
                            Body = new List<AdaptiveElement>()
                            {
                                new AdaptiveTextBlock($"Notification for {patientSymptomInfo.Doctor}")
                                {
                                    Weight = AdaptiveTextWeight.Bolder,
                                    Size = AdaptiveTextSize.Medium
                                },
                                new AdaptiveColumnSet()
                                {
                                    Columns = new List<AdaptiveColumn>
                                    {
                                        new AdaptiveColumn()
                                        {
                                            Width = "auto",
                                            Items = new List<AdaptiveElement>()
                                            {
                                                new AdaptiveImage("https://cdn1.iconfinder.com/data/icons/medical-health-care-thick-colored-version/33/male_patient-512.png")
                                                {
                                                    Size = AdaptiveImageSize.Small,
                                                    Style = AdaptiveImageStyle.Person
                                                }
                                            }
                                        },
                                        new AdaptiveColumn()
                                        {
                                            Width = "stretch",
                                            Items = new List<AdaptiveElement>()
                                            {
                                                new AdaptiveTextBlock(patientSymptomInfo.PatientName)
                                                {
                                                    Weight = AdaptiveTextWeight.Bolder,
                                                    Wrap = true
                                                },
                                                new AdaptiveTextBlock(patientSymptomInfo.PatientDob)
                                                {
                                                    Wrap = true,
                                                    IsSubtle = true,
                                                    Spacing = AdaptiveSpacing.None
                                                }
                                            }
                                        }
                                    },

                                },
                                new AdaptiveTextBlock(patientSymptomInfo.Symptoms)
                                {
                                    Wrap = true,
                                    IsSubtle = true
                                },
                                new AdaptiveFactSet()
                                {
                                    Facts = new List<AdaptiveFact>()
                                    {
                                        new AdaptiveFact("Symptom", patientSymptomInfo.SignSymptomMention),
                                        new AdaptiveFact("Medication", patientSymptomInfo.MedicationMention),
                                        new AdaptiveFact("Disease", patientSymptomInfo.DiseaseDisorderMention),
                                        new AdaptiveFact("Anatomical", patientSymptomInfo.AnatomicalSiteMention),
                                    }
                                }
                            },
                            Actions = new List<AdaptiveAction>()
                            {
                                new AdaptiveSubmitAction()
                                {
                                     Title = "Send to EMR",
                                     Id = "sendToEmr",
                                     DataJson = JsonConvert.SerializeObject(patientSymptomInfo)
                                }
                            }
                        };

                        await connectorClient.Conversations.SendToConversationAsync(conversationResource.Id, 
                        (Activity)MessageFactory.Attachment(new Attachment
                        {
                            ContentType = AdaptiveCard.ContentType,
                            Content = JObject.FromObject(adaptiveCard),
                        }));

                    }, default(CancellationToken));
                }
            }
            
            // Let the caller know proactive messages have been sent
            return new ContentResult()
            {
                Content = "<html><body><h1>Proactive messages have been sent.</h1></body></html>",
                ContentType = "text/html",
                StatusCode = (int)HttpStatusCode.OK,
            };
        }
    }
}
