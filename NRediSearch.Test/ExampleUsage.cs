﻿using System;
using Xunit;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using System.IO;

namespace NRediSearch.Test
{
    public class ExampleUsage : IDisposable
    {
        private ConnectionMultiplexer conn;
        private IDatabase db;

        private ITestOutputHelper Output { get; }
        public ExampleUsage(ITestOutputHelper output)
        {
            Output = output;
            const string ep = "127.0.0.1:6379";
            var options = new ConfigurationOptions
            {
                EndPoints = {ep},
                AllowAdmin = true
            };
            conn = ConnectionMultiplexer.Connect(options);
            db = conn.GetDatabase();

            var server = conn.GetServer(ep);
            var arr = (RedisResult[])server.Execute("module", "list");
            bool found = false;
            foreach(var module in arr)
            {
                var parsed = Parse(module);
                if(parsed.TryGetValue("name", out var val) && val == "ft")
                {
                    found = true;
                    if(parsed.TryGetValue("ver", out val))
                        Output.WriteLine($"Version: {val}");
                    break;
                }      
            }

            if (!found)
            {
                Output.WriteLine("Module not found; attempting to load...");
                var config = server.Info("server").SelectMany(_ => _).FirstOrDefault(x => x.Key == "config_file").Value;
                if(!string.IsNullOrEmpty(config))
                {
                    var i = config.LastIndexOf('/');
                    var modulePath = config.Substring(0, i + 1) + "redisearch.so";
                    var result = server.Execute("module", "load", modulePath);
                    Output.WriteLine((string)result);
                }
            }
        }
        static Dictionary<string, RedisValue> Parse(RedisResult module)
        {
            var data = new Dictionary<string, RedisValue>();
            var lines = (RedisResult[])module;
            for(int i = 0; i < lines.Length;)
            {
                var key = (string)lines[i++];
                var value = (RedisValue)lines[i++];
                data[key] = value;
            }
            return data;
        }

        public void Dispose()
        {
            conn?.Dispose();
            conn = null;
            db = null;
        }

        [Fact]
        public void BasicUsage()
        {
            var client = new Client("testung", db);

            try { client.DropIndex(); } catch { } // reset DB

            // Defining a schema for an index and creating it:
            var sc = new Schema()
                .AddTextField("title", 5.0)
                .AddTextField("body", 1.0)
                .AddNumericField("price");

            bool result = false;
            try
            {
                result = client.CreateIndex(sc, Client.IndexOptions.Default);
            }
            catch (RedisServerException ex)
            {
                // TODO: Convert to Skip
                if (ex.Message == "ERR unknown command 'FT.CREATE'")
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine("Module not installed, aborting");
                }
                throw;
            }

            Assert.True(result);

            // note: using java API equivalent here; it would be nice to
            // use meta-programming / reflection instead in .NET

            // Adding documents to the index:
            var fields = new Dictionary<string, RedisValue>
            {
                ["title"] = "hello world",
                ["body"] = "lorem ipsum",
                ["price"] = 1337
            };

            Assert.True(client.AddDocument("doc1", fields));

            // Creating a complex query
            var q = new Query("hello world")
                .AddFilter(new Query.NumericFilter("price", 1300, 1350))
                .Limit(0, 5);

            // actual search
            var res = client.Search(q);

            Assert.Equal(1, res.TotalResults);
            var item = res.Documents.Single();
            Assert.Equal("doc1", item.Id);

            Assert.True(item.HasProperty("title"));
            Assert.True(item.HasProperty("body"));
            Assert.True(item.HasProperty("price"));
            Assert.False(item.HasProperty("blap"));

            Assert.Equal("hello world", (string)item["title"]);
            Assert.Equal("lorem ipsum", (string)item["body"]);
            Assert.Equal(1337, (int)item["price"]);
        }
    }
}
