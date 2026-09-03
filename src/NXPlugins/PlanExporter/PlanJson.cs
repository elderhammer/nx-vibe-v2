// PlanJson.cs — plan 文档 JSON 序列化（POST-4：double 原样 round-trip；POST-1：可再解析复验）
// 实现：DataContractJsonSerializer（.NET Framework 内置，无外部依赖）。序列化失败 → 抛异常，
// 由 PlanWriter 转为原子写失败（POST-2）。

using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace NXPlugins.PlanExporter
{
    /// <summary>写盘/复验的序列化接缝（POST-2 注入失败用）。</summary>
    public interface IPlanSerializer
    {
        string Serialize(PlanDocument doc);
        PlanDocument Deserialize(string json);
    }

    public sealed class PlanJsonSerializer : IPlanSerializer
    {
        public string Serialize(PlanDocument doc)
        {
            var ser = new DataContractJsonSerializer(typeof(PlanDocument));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, doc);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public PlanDocument Deserialize(string json)
        {
            var ser = new DataContractJsonSerializer(typeof(PlanDocument));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (PlanDocument)ser.ReadObject(ms);
            }
        }
    }
}
