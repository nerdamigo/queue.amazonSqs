using NerdAmigo.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using NerdAmigo.Common.Aws;
using NerdAmigo.Queue.Common;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Collections.Concurrent;

namespace NerdAmigo.Queue.AmazonSqs
{
	public class SQSClient<TMessage> : IQueueClient<TMessage> where TMessage : IQueueMessage
	{
		private IConfigurationProvider<AwsClientConfiguration> ConfigurationProvider;
		private AmazonSQSClient AwsClient;
		private IQueueIdentifierProvider QueueIdentifierProvider;
		private ConcurrentDictionary<string, Message> MessageIdentifierIndex;
		private const int MAX_RECEIVE_POLL_WAIT_MS = 60000;
		private const int MIN_RECEIVE_POLL_WAIT_MS = 1000;
		private int ReceieveMissCount;

		public SQSClient(
			IQueueIdentifierProvider QueueIdentifierProvider,
			IConfigurationProvider<AwsClientConfiguration> ConfigurationProvider)
		{
			this.ConfigurationProvider = ConfigurationProvider;
			this.QueueIdentifierProvider = QueueIdentifierProvider;
			this.MessageIdentifierIndex = new ConcurrentDictionary<string, Message>();
			this.ReceieveMissCount = 0;

			ConfigurationProvider.ConfigurationUpdated(ReloadConfiguration);
			ReloadConfiguration(ConfigurationProvider.CurrentConfiguration);
		}

		private void ReloadConfiguration(AwsClientConfiguration Configuration)
		{
			AmazonSQSClient awsClient = new AmazonSQSClient(
				this.ConfigurationProvider.CurrentConfiguration.AccessKeyId,
				this.ConfigurationProvider.CurrentConfiguration.SecretAccessKey,
				this.ConfigurationProvider.CurrentConfiguration.GetEndpoint());

			this.AwsClient = awsClient;
		}

		private async Task<string> GetQueueUrl(CancellationToken cancellationToken)
		{
			QueueIdentifier queueId = this.QueueIdentifierProvider.GetIdentifier<TMessage>();
			CreateQueueRequest createQueueRequest = new CreateQueueRequest(queueId.QueueName);
			CreateQueueResponse createQueueResponse = await this.AwsClient.CreateQueueAsync(createQueueRequest, cancellationToken);
			return createQueueResponse.QueueUrl;
		}

		public async Task<TMessage> Enqueue(TMessage Message, CancellationToken cancellationToken)
		{
			string serializedMessage = JsonConvert.SerializeObject(Message);
			string queueUrl = await GetQueueUrl(cancellationToken);

			SendMessageResponse sendMessageResponse = this.AwsClient.SendMessage(queueUrl, serializedMessage);

			Message.MessageId = new QueueMessageIdentifier(sendMessageResponse.MessageId);

			return Message;
		}

		public async Task<TMessage> Receive(CancellationToken cancellationToken)
		{
			QueueIdentifier queueId = this.QueueIdentifierProvider.GetIdentifier<TMessage>();
			string queueUrl = await GetQueueUrl(cancellationToken);

			ReceiveMessageRequest receiveMessageRequest = new ReceiveMessageRequest(queueUrl);
			receiveMessageRequest.MaxNumberOfMessages = 1;

			Message sqsMessage;
			while (true)
			{
				ReceiveMessageResponse receiveMessageResponse = await this.AwsClient.ReceiveMessageAsync(receiveMessageRequest, cancellationToken);
				if (receiveMessageResponse.Messages.Count > 0)
				{
					sqsMessage = receiveMessageResponse.Messages[0];
					if(ReceieveMissCount < 2)
					{
						ReceieveMissCount = 0;
					}
					else
					{
						ReceieveMissCount = ReceieveMissCount / 2;
					}
					break;
				}

				//no message received, back off a bit (in case receive message wait time is not set)
				ReceieveMissCount++;
				int pauseMs = (int)Math.Max(MIN_RECEIVE_POLL_WAIT_MS, Math.Min(MAX_RECEIVE_POLL_WAIT_MS, Math.Pow(2, ReceieveMissCount) * 100));
				cancellationToken.WaitHandle.WaitOne(pauseMs);
			}

			this.MessageIdentifierIndex.TryAdd(sqsMessage.MessageId, sqsMessage);

			TMessage message = JsonConvert.DeserializeObject<TMessage>(sqsMessage.Body);
			message.MessageId = new QueueMessageIdentifier(sqsMessage.MessageId);

			return message;
		}

		public async Task Delete(TMessage Message, CancellationToken cancellationToken)
		{
			if (Message == null || Message.MessageId == null || String.IsNullOrWhiteSpace(Message.MessageId.MessageID))
			{
				throw new ArgumentNullException("Message", "MessageID not available on Message");
			}

			string originalId = Message.MessageId.MessageID;
			Message sqsMessage;
			if (this.MessageIdentifierIndex.TryGetValue(originalId, out sqsMessage))
			{
				string queueUrl = await GetQueueUrl(cancellationToken);
				DeleteMessageRequest deleteMessageRequest = new DeleteMessageRequest(queueUrl, sqsMessage.ReceiptHandle);
				DeleteMessageResponse deleteMessageResponse = await this.AwsClient.DeleteMessageAsync(deleteMessageRequest, cancellationToken);
			}
		}
	}
}
