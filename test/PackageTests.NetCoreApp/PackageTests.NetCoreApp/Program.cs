using Microsoft.OData;
using Microsoft.OData.Edm;
using System;
using System.Xml;
using Xunit;

namespace PackageTests.NetCoreApp
{
    class Program
    {
        static Uri baseUri = new Uri("https://services.odata.org/V4/(S(f1yueljyzoy0bfv5deqdqkdq))/TrippinServiceRW/");
        static string nameSpace = "Microsoft.OData.SampleService.Models.TripPin";
        static Microsoft.OData.Edm.IEdmModel Model { get; set; }

        static void Main(string[] args)
        {
            BasicTest();
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        public static void BasicTest()
        {
            Model = GetModel();

            // create entry and insert
            var personEntry = new ODataResource() { TypeName = nameSpace + ".Person" };
            var userName = new ODataProperty { Name = "UserName", Value = "Test" };
            var firstName = new ODataProperty { Name = "FirstName", Value = "Test" };
            var lastName = new ODataProperty { Name = "LastName", Value = "1" };
            var emails = new ODataProperty { Name = "Emails", Value = new ODataCollectionValue() };

            personEntry.Properties = new[] { userName, firstName, lastName, emails };

            var settings = new ODataMessageWriterSettings();
            settings.BaseUri = baseUri;

            var personType = Model.FindDeclaredType(nameSpace + ".Person") as IEdmEntityType;
            var people = Model.EntityContainer.FindEntitySet("People");

            var requestMessage = new HttpWebRequestMessage(new Uri(settings.BaseUri + "People"));
            requestMessage.SetHeader("Content-Type", "application/json");
            requestMessage.SetHeader("Accept", "application/json");
            requestMessage.Method = "POST";
            using (var messageWriter = new ODataMessageWriter(requestMessage, settings))
            {
                var odataWriter = messageWriter.CreateODataResourceWriter(people, personType);
                odataWriter.WriteStart(personEntry);
                odataWriter.WriteEnd();
            }

            var responseMessage = requestMessage.GetResponse();

            int expectedStatus = 201;
            Assert.True(expectedStatus.Equals(responseMessage.StatusCode));
            Console.WriteLine($"rspCode: {responseMessage.StatusCode}");
        }

        public static IEdmModel GetModel()
        {
            HttpWebRequestMessage message = new HttpWebRequestMessage(new Uri(baseUri.AbsoluteUri + "$metadata", UriKind.Absolute));
            message.SetHeader("Accept", "application/xml");

            using (var messageReader = new ODataMessageReader(message.GetResponse()))
            {
                Func<Uri, XmlReader> getReferencedSchemaFunc = uri =>
                {
                    HttpWebRequestMessage msg = new HttpWebRequestMessage(new Uri(uri.AbsoluteUri, UriKind.Absolute));
                    return XmlReader.Create(msg.GetResponse().GetStream());
                };
                return messageReader.ReadMetadataDocument(getReferencedSchemaFunc);
            }
        }
    }
}
