﻿namespace NServiceBus.Transport.RabbitMQ.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using RabbitMqNext;

    [TestFixture]
    class MessageConverterTests
    {
        MessageConverter converter = new MessageConverter();

        [Test]
        public void TestCanHandleNoInterestingProperties()
        {
            var message = new MessageDelivery
            {
                properties = new BasicProperties
                {
                    MessageId = "Blah"
                }
            };

            var headers = converter.RetrieveHeaders(message);
            var messageId = converter.RetrieveMessageId(message);

            Assert.IsNotNull(messageId);
            Assert.IsNotNull(headers);
        }

        [Test]
        public void Should_throw_exception_when_no_message_id_is_set()
        {
            var message = new MessageDelivery
            {
                properties = new BasicProperties()
            };

            var headers = new Dictionary<string, string>();

            Assert.Throws<InvalidOperationException>(() => converter.RetrieveMessageId(message));
        }

        [Test]
        public void Should_throw_exception_when_using_custom_strategy_and_no_message_id_is_returned()
        {
            var customConverter = new MessageConverter(args => "");

            var message = new MessageDelivery
            {
                properties = new BasicProperties()
            };

            var headers = new Dictionary<string, string>();

            Assert.Throws<InvalidOperationException>(() => customConverter.RetrieveMessageId(message));
        }

        [Test]
        public void Should_fall_back_to_message_id_header_when_custom_strategy_returns_empty_string()
        {
            var customConverter = new MessageConverter(args => "");

            var message = new MessageDelivery
            {
                properties = new BasicProperties()
            };

            message.properties.Headers[Headers.MessageId] = "Blah";

            var messageId = customConverter.RetrieveMessageId(message);

            Assert.AreEqual(messageId, "Blah");
        }

        [Test]
        public void TestCanHandleByteArrayHeader()
        {
            var message = new MessageDelivery
            {
                properties = new BasicProperties()
                {
                    MessageId = "Blah",
                }
            };
            message.properties.Headers["Foo"] = Encoding.UTF8.GetBytes("blah");

            var headers = converter.RetrieveHeaders(message);

            Assert.NotNull(headers);
            Assert.AreEqual("blah", headers["Foo"]);
        }

        [Test]
        public void Should_set_replyto_header_if_native_replyto_is_present()
        {
            var message = new MessageDelivery
            {
                properties = new BasicProperties()
                {
                    ReplyTo = "myaddress",
                    MessageId = "Blah"
                }
            };

            var headers = converter.RetrieveHeaders(message);

            Assert.NotNull(headers);
            Assert.AreEqual("myaddress", headers[Headers.ReplyToAddress]);
        }

        [Test]
        public void Should_override_replyto_header_if_native_replyto_is_present()
        {
            var message = new MessageDelivery
            {
                properties = new BasicProperties()
                {
                    ReplyTo = "myaddress",
                    MessageId = "Blah",
                }
            };
            message.properties.Headers[Headers.ReplyToAddress] = Encoding.UTF8.GetBytes("nsb set address");

            var headers = converter.RetrieveHeaders(message);

            Assert.NotNull(headers);
            Assert.AreEqual("myaddress", headers[Headers.ReplyToAddress]);
        }

        [Test]
        public void TestCanHandleStringHeader()
        {
            var message = new MessageDelivery
            {
                properties = new BasicProperties()
                {
                    MessageId = "Blah",
                }
            };
            message.properties.Headers["Foo"] = "ni";

            var headers = converter.RetrieveHeaders(message);

            Assert.NotNull(headers);
            Assert.AreEqual("ni", headers["Foo"]);
        }

        [Test]
        public void TestCanHandleStringArrayListsHeader()
        {
            var message = new MessageDelivery
            {
                properties = new BasicProperties()
                {
                    MessageId = "Blah",
                }
            };
            message.properties.Headers["Foo"] = new ArrayList
            {
                "Bing"
            };

            var headers = converter.RetrieveHeaders(message);

            Assert.NotNull(headers);
            Assert.AreEqual("Bing", headers["Foo"]);
        }

        [Test]
        public void TestCanHandleStringObjectListHeader()
        {
            var message = new MessageDelivery
            {
                properties = new BasicProperties()
                {
                    MessageId = "Blah",
                }
            };
            message.properties.Headers["Foo"] = new List<object>
            {
                "Bing"
            };

            var headers = converter.RetrieveHeaders(message);

            Assert.NotNull(headers);
            Assert.AreEqual("Bing", headers["Foo"]);
        }

        [Test]
        public void TestCanHandleTablesListHeader()
        {
            var message = new MessageDelivery
            {
                properties = new BasicProperties()
                {
                    MessageId = "Blah"
                }
            };
            message.properties.Headers["Foo"]= new List<object> { new Dictionary<string, object> { { "key1", Encoding.UTF8.GetBytes("value1") }, { "key2", Encoding.UTF8.GetBytes("value2") } } };

        var headers = converter.RetrieveHeaders(message);

            Assert.NotNull(headers);
            Assert.AreEqual("key1=value1,key2=value2", Convert.ToString(headers["Foo"]));
        }
    }
}
