using NerdAmigo.Abstractions;
using NerdAmigo.Common.Cli;
using NerdAmigo.Common.SimpleInjector;
using NerdAmigo.Queue.Common;
using SimpleInjector;
using SimpleInjector.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NerdAmigo.Queue.AmazonSqs.TestCli
{
	class Program
	{
		static void Main(string[] args)
		{
			Container container = new Container();
			container.Register<ILogger, NerdAmigo.Abstractions.Default.DefaultLogger>(Lifestyle.Singleton);
			container.Register<IPathMapper, CliPathMapper>(Lifestyle.Singleton);
			container.Register(typeof(IConfigurationProvider<>), typeof(NerdAmigo.Common.Configuration.ConfigurationProvider<>), Lifestyle.Singleton);

			container.Register<IQueueIdentifierProvider, QueueIdentifierProvider>(Lifestyle.Singleton);
			container.Register(typeof(IQueueClient<>), typeof(SQSClient<>), Lifestyle.Singleton);
			container.Register(typeof(IQueueMessageWorker<>), new[] { System.Reflection.Assembly.GetExecutingAssembly() });
			container.Register(typeof(QueueMessageProcessor<>), new[] { System.Reflection.Assembly.GetExecutingAssembly() });

			container.RegisterDecorator(typeof(IQueueClient<>), typeof(QueueClientLogDecorator<>));

			container.Verify();
			
			IQueueClient<SampleMessage> queueClient = container.GetInstance<IQueueClient<SampleMessage>>();
			using (CancellationTokenSource cts = new CancellationTokenSource())
			{
				Task sender = Task.Factory.StartNew(() =>
				{
					int idx = 0;
					Random r = new Random();
					while (!cts.Token.IsCancellationRequested)
					{
						queueClient.Enqueue(new SampleMessage() { Name = DateTime.Now.ToShortTimeString(), Id = ++idx }, cts.Token);

						cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(r.Next(60)));
					}
				}, cts.Token);

				QueueMessageProcessor<SampleMessage> messageProcessor = container.GetInstance<QueueMessageProcessor<SampleMessage>>();
				messageProcessor.QueueMessageWorkerActivator = new SimpleInjectorQueueMessageWorkerActivator(container);
				messageProcessor.Begin(cts.Token);

				Console.ReadKey();
				cts.Cancel();

				try
				{
					sender.Wait();
				}
				catch (AggregateException ex)
				{
					foreach (Exception iEx in ex.InnerExceptions)
					{
						Console.WriteLine("Task Exception: " + iEx.Message);
					}
				}
			}
		}
	}
}
