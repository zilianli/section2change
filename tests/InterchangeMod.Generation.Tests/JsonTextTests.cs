using System.Text.Json;
using InterchangeMod.Generation.Text;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class JsonTextTests
    {
        [Fact]
        public void MixedFields_ProduceValidJson()
        {
            string json = new JsonText()
                .BeginObject()
                .Field("n", 1.5f)
                .Field("i", 3)
                .Field("b", true)
                .Field("s", "Highway Oneway - 2 lanes")
                .BeginObject("inner").Field("x", -10f).EndObject()
                .BeginArray("list").Item("a").Item("b").EndArray()
                .BeginArray("objects")
                .BeginObject().Field("k", 1).EndObject()
                .BeginObject().Field("k", 2).EndObject()
                .EndArray()
                .Field("after", false)
                .EndObject()
                .ToString();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal(1.5, root.GetProperty("n").GetDouble());
            Assert.Equal(3, root.GetProperty("i").GetInt32());
            Assert.True(root.GetProperty("b").GetBoolean());
            Assert.Equal("Highway Oneway - 2 lanes", root.GetProperty("s").GetString());
            Assert.Equal(-10, root.GetProperty("inner").GetProperty("x").GetDouble());
            Assert.Equal(2, root.GetProperty("list").GetArrayLength());
            Assert.Equal(2, root.GetProperty("objects")[1].GetProperty("k").GetInt32());
            Assert.False(root.GetProperty("after").GetBoolean());
        }

        [Fact]
        public void Strings_AreEscaped()
        {
            string json = new JsonText().BeginObject().Field("s", "a\"b\\c\nd").Field("nul", (string?)null).EndObject().ToString();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("a\"b\\c\nd", doc.RootElement.GetProperty("s").GetString());
            Assert.Equal("", doc.RootElement.GetProperty("nul").GetString());
        }

        [Fact]
        public void Numbers_UseInvariantCulture()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                // 德语等区域用逗号作小数点，JSON 必须始终用点
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                string json = new JsonText().BeginObject().Field("n", 2.5f).EndObject().ToString();
                Assert.Equal("{\"n\":2.5}", json);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }
    }
}
