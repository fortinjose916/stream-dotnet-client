using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Stream.Client;
using RabbitMQ.Stream.Client.AMQP;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Tests
{
    public sealed class EmbeddedResourceDataAttribute : DataAttribute
    {
        private readonly string[] _args;

    
        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            var result = new object[_args.Length];
            for (var index = 0; index < _args.Length; index++)
            {
                result[index] = ReadManifestData(_args[index]);
            }

            return new[] {result};
        }

        public static string ReadManifestData(string resourceName)
        {
            var assembly = typeof(EmbeddedResourceDataAttribute).GetTypeInfo().Assembly;
            resourceName = resourceName.Replace("/", ".");
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Could not load manifest resource stream.");
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class WaitTestBeforeAfter : BeforeAfterTestAttribute
    {
        public override void Before(MethodInfo methodUnderTest)
        {
            Thread.Sleep(200);
        }

        public override void After(MethodInfo methodUnderTest)
        {
            Thread.Sleep(200);
        }
    }

    public class Utils<TResult>
    {
        private readonly ITestOutputHelper testOutputHelper;

        public Utils(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper;
        }


        // Added this generic function to trap error during the 
        // tests
        public void WaitUntilTaskCompletes(TaskCompletionSource<TResult> tasks,
            bool expectToComplete = true,
            int timeOut = 10000)
        {
            try
            {
                var resultTestWait = tasks.Task.Wait(timeOut);
                Assert.Equal(resultTestWait, expectToComplete);
            }
            catch (Exception e)
            {
                testOutputHelper.WriteLine($"wait until task completes error #{e}");
                throw;
            }
        }
    }


    public static class SystemUtils
    {
        public static void Wait()
        {
            Thread.Sleep(500);
        }

        public static async Task PublishMessages(StreamSystem system, string stream, int numberOfMessages,
            ITestOutputHelper testOutputHelper)
        {
            var testPassed = new TaskCompletionSource<int>();
            var count = 0;
            var producer = await system.CreateProducer(
                new ProducerConfig
                {
                    Reference = "producer",
                    Stream = stream,
                    ConfirmHandler = confirmation =>
                    {
                        count++;
                        testOutputHelper.WriteLine($"Published and Confirmed: {count} messages");
                        if (count != numberOfMessages) return;
                        testPassed.SetResult(count);
                    }
                });

            for (var i = 0; i < numberOfMessages; i++)
            {
                var msgData = new Data($"message_{i}".AsReadonlySequence());
                var message = new Message(msgData);
                await producer.Send(Convert.ToUInt64(i), message);
            }

            testPassed.Task.Wait(10000);
            Assert.Equal(producer.Client.MessagesSent, numberOfMessages);
            Assert.True(producer.Client.ConfirmFrames >= 1);
            producer.Dispose();
        }

        public static void PostDefinition(string json)
        {
            var httpWebRequest = (HttpWebRequest) WebRequest.Create("http://localhost:15672/api/definitions");
            httpWebRequest.Credentials = new System.Net.NetworkCredential("guest", "guest");
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Method = "POST";

            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                streamWriter.Write(json);
            }

            var httpResponse = (HttpWebResponse) httpWebRequest.GetResponse();
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
            {
                var result = streamReader.ReadToEnd();
            }
        }

        public static string GetFileContent(string fileName)
        {
            
            var codeBaseUrl = new Uri(Assembly.GetExecutingAssembly().Location);
            var codeBasePath = Uri.UnescapeDataString(codeBaseUrl.AbsolutePath);
            var dirPath = Path.GetDirectoryName(codeBasePath);
            if (dirPath != null)
            {
                var filename =  Path.Combine(dirPath, "Resources", "definition_test.json");


                Task<string> fileTask =  File.ReadAllTextAsync(filename);
                fileTask.Wait(1000);
                return fileTask.Result;
            }

            return "";
        }
        
        
    }
}