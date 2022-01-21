using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Stream.Client;
using RabbitMQ.Stream.Client.AMQP;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    [Collection("Sequential")]
    public class ConsumerSystemTests
    {
        private readonly ITestOutputHelper testOutputHelper;

        public ConsumerSystemTests(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper;
        }


        [Fact]
        [WaitTestBeforeAfter]
        public async void CreateConsumer()
        {
            var testPassed = new TaskCompletionSource<Data>();
            var stream = Guid.NewGuid().ToString();
            var config = new StreamSystemConfig();
            var system = await StreamSystem.Create(config);
            await system.CreateStream(new StreamSpec(stream));
            var producer = await system.CreateProducer(
                new ProducerConfig
                {
                    Reference = "producer",
                    Stream = stream
                });
            var consumer = await system.CreateConsumer(
                new ConsumerConfig
                {
                    Reference = "consumer",
                    Stream = stream,
                    MessageHandler = async (consumer, ctx, message) =>
                    {
                        testPassed.SetResult(message.Data);
                        await Task.CompletedTask;
                    }
                });
            var msgData = new Data("apple".AsReadonlySequence());
            var message = new Message(msgData);
            await producer.Send(1, message);
            //wait for sent message to be delivered

            new Utils<Data>(testOutputHelper).WaitUntilTaskCompletes(testPassed);


            Assert.Equal(msgData.Contents.ToArray(), testPassed.Task.Result.Contents.ToArray());
            producer.Dispose();
            consumer.Dispose();
            await system.DeleteStream(stream);
            await system.Close();
        }

        [Fact]
        [WaitTestBeforeAfter]
        public async void CloseProducerTwoTimesShouldBeOk()
        {
            var stream = Guid.NewGuid().ToString();
            var config = new StreamSystemConfig();
            var system = await StreamSystem.Create(config);
            await system.CreateStream(new StreamSpec(stream));
            var consumer = await system.CreateConsumer(
                new ConsumerConfig
                {
                    Reference = "consumer",
                    Stream = stream,
                    MessageHandler = async (consumer, ctx, message) => { await Task.CompletedTask; }
                });

            Assert.Equal(ResponseCode.Ok, await consumer.Close());
            Assert.Equal(ResponseCode.Ok, await consumer.Close());
            consumer.Dispose();
            await system.DeleteStream(stream);
            await system.Close();
        }


        [Fact]
        [WaitTestBeforeAfter]
        public async void ConsumerStoreOffset()
        {
            var testPassed = new TaskCompletionSource<int>();
            var stream = Guid.NewGuid().ToString();
            var config = new StreamSystemConfig();
            var system = await StreamSystem.Create(config);
            await system.CreateStream(new StreamSpec(stream));
            const int numberOfMessages = 10;
            await SystemUtils.PublishMessages(system, stream, numberOfMessages, testOutputHelper);
            var count = 0;
            var consumer = await system.CreateConsumer(
                new ConsumerConfig
                {
                    Reference = "consumer_offset",
                    Stream = stream,
                    OffsetSpec = new OffsetTypeFirst(),
                    MessageHandler = async (consumer, ctx, message) =>
                    {
                        testOutputHelper.WriteLine($"ConsumerStoreOffset receiving.. {count}");
                        count++;
                        if (count == numberOfMessages)
                        {
                            await consumer.StoreOffset(ctx.Offset);
                            testOutputHelper.WriteLine($"ConsumerStoreOffset done: {count}");
                            testPassed.SetResult(numberOfMessages);
                        }

                        await Task.CompletedTask;
                    }
                });

            new Utils<int>(testOutputHelper).WaitUntilTaskCompletes(testPassed);


            // // Here we use the standard client to check the offest
            // // since client.QueryOffset/2 is hidden in the System
            //
            var clientParameters = new ClientParameters { };
            var client = await Client.Create(clientParameters);
            var offset = await client.QueryOffset("consumer_offset", stream);
            // The offset must be numberOfMessages less one
            Assert.Equal(offset.Offset, Convert.ToUInt64(numberOfMessages - 1));
            await consumer.Close();
            await system.DeleteStream(stream);
            await system.Close();
        }


        [Fact]
        [WaitTestBeforeAfter]
        public async void NotifyConsumerClose()
        {
            var stream = Guid.NewGuid().ToString();
            var config = new StreamSystemConfig();
            var system = await StreamSystem.Create(config);
            await system.CreateStream(new StreamSpec(stream));
            var testPassed = new TaskCompletionSource<bool>();
            var consumer = await system.CreateConsumer(
                new ConsumerConfig
                {
                    Reference = "consumer",
                    Stream = stream,
                    MessageHandler = async (consumer, ctx, message) => { await Task.CompletedTask; },
                    ConnectionClosedHandler = async s =>
                    {
                        testOutputHelper.WriteLine("NotifyConsumerClose set true");
                        testPassed.SetResult(true);
                        await Task.CompletedTask;
                    }
                });

            Assert.Equal(ResponseCode.Ok, await consumer.Close());
            new Utils<bool>(testOutputHelper).WaitUntilTaskCompletes(testPassed);
            await system.DeleteStream(stream);
            await system.Close();
        }


        [Fact]
        [WaitTestBeforeAfter]
        public async void CreateProducerConsumerAddressResolver()
        {
            var testPassed = new TaskCompletionSource<Data>();
            var stream = Guid.NewGuid().ToString();
            AddressResolver addressResolver = new AddressResolver(new IPEndPoint(IPAddress.Loopback, 5552));
            var config = new StreamSystemConfig()
            {
                AddressResolver = addressResolver,
            };
            var system = await StreamSystem.Create(config);
            await system.CreateStream(new StreamSpec(stream));
            var producer = await system.CreateProducer(
                new ProducerConfig
                {
                    Reference = "producer",
                    Stream = stream
                });
            var consumer = await system.CreateConsumer(
                new ConsumerConfig
                {
                    Reference = "consumer",
                    Stream = stream,
                    MessageHandler = async (consumer, ctx, message) =>
                    {
                        testPassed.SetResult(message.Data);
                        await Task.CompletedTask;
                    }
                });
            var msgData = new Data("apple".AsReadonlySequence());
            var message = new Message(msgData);
            await producer.Send(1, message);
            //wait for sent message to be delivered

            new Utils<Data>(testOutputHelper).WaitUntilTaskCompletes(testPassed);


            Assert.Equal(msgData.Contents.ToArray(), testPassed.Task.Result.Contents.ToArray());
            producer.Dispose();
            consumer.Dispose();
            await system.DeleteStream(stream);
            await system.Close();
        }


        [Fact]
        [WaitTestBeforeAfter]
        public async void ProducerAndConsumerCompressShouldHaveTheSameMessages()
        {
            void PumpMessages(ICollection<Message> messages, string prefix)
            {
                for (var i = 0; i < 5; i++)
                {
                    messages.Add(new Message(Encoding.UTF8.GetBytes($"{prefix}_{i}")));
                }
            }

            void AssertMessages(IReadOnlyList<Message> expected, IReadOnlyList<Message> actual)
            {
                for (var i = 0; i < 5; i++)
                {
                    Assert.Equal(expected[i].Data.Contents.ToArray(), actual[i].Data.Contents.ToArray());
                }
            }

            var testPassed = new TaskCompletionSource<List<Message>>();
            var stream = Guid.NewGuid().ToString();
            var config = new StreamSystemConfig();
            var system = await StreamSystem.Create(config);
            await system.CreateStream(new StreamSpec(stream));

            var receivedMessages = new List<Message>();
            var consumer = await system.CreateConsumer(
                new ConsumerConfig
                {
                    Reference = "consumer",
                    Stream = stream,
                    MessageHandler = async (consumer, ctx, message) =>
                    {
                        receivedMessages.Add(message);
                        if (receivedMessages.Count == 10)
                        {
                            testPassed.SetResult(receivedMessages);
                        }

                        await Task.CompletedTask;
                    }
                });


            var producer = await system.CreateProducer(
                new ProducerConfig
                {
                    Reference = "producer",
                    Stream = stream
                });

            var messagesNone = new List<Message>();
            PumpMessages(messagesNone, "None");
            await producer.Send(1, messagesNone, CompressionType.None);

            var messagesGzip = new List<Message>();
            PumpMessages(messagesGzip, "Gzip");
            await producer.Send(2, messagesGzip, CompressionType.Gzip);
            
            new Utils<List<Message>>(testOutputHelper).WaitUntilTaskCompletes(testPassed);

            AssertMessages(messagesNone, testPassed.Task.Result.FindAll(s =>
                Encoding.Default.GetString(s.Data.Contents.ToArray()).Contains("None_")));

            AssertMessages(messagesGzip, testPassed.Task.Result.FindAll(s =>
                Encoding.Default.GetString(s.Data.Contents.ToArray()).Contains("Gzip_")));
            
            producer.Dispose();
            consumer.Dispose();
            await system.DeleteStream(stream);
            await system.Close();
        }
    }
}