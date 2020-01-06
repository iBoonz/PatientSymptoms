using AdaptiveCards;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Xml.Serialization;

namespace ProactiveBot.Extensions
{
    public class AdaptiveInvokeAction : AdaptiveAction
    {
        public const string TypeName = "Invoke";

        public override string Type { get; set; } = TypeName;


        public object Data { get; set; }


        public string DataJson
        {
            get
            {
                if (Data != null)
                    return JsonConvert.SerializeObject(Data, Formatting.Indented);
                return null;
            }
            set
            {
                if (value == null)
                    Data = null;
                else
                    Data = JsonConvert.DeserializeObject(value, new JsonSerializerSettings
                    {
                    });
            }
        }
    }
}
