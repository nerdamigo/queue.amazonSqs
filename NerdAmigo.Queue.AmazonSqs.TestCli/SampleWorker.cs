using NerdAmigo.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NerdAmigo.Queue.AmazonSqs.TestCli
{
	public class SampleWorker : IQueueMessageWorker<SampleMessage>
	{
		private ILogger Logger;
		public SampleWorker(ILogger Logger)
		{
			this.Logger = Logger;
			Logger.Log(new LogEntry(LogEventSeverity.Debug, "test"));
		}

		public void Execute(SampleMessage Message)
		{
			Console.WriteLine(String.Format("Sample Message being worked on: ID: {0}, Name: {1}", Message.Id, Message.Name));
			System.Threading.Thread.Sleep(1000);
			Console.WriteLine(String.Format("Sample Message work done: ID: {0}, Name: {1}", Message.Id, Message.Name));
		}
	}
}
