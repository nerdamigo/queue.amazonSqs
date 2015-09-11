using NerdAmigo.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NerdAmigo.Queue.AmazonSqs.TestCli
{
	public class SampleMessage : IQueueMessage
	{
		public string Name { get; set; }
		public int Id { get; set; }

		public QueueMessageIdentifier MessageId { get; set; }

		public override string ToString()
		{
			return String.Format("Sample Message {0}, {1}", this.Id, this.Name);
		}
	}
}
