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
            // V15-INV-1：旧形状（v1 数据合同）strategy/technology KV 数组的 Value = 裸 JSON number，
            // 在 union 字典类型下直解必抛（P0 实证，DCJS 状态机错）→ 先归一为 {"N":<n>} 包装再解。
            // 自产新形状 Value 首字符为 '{' → 不命中 → shim 幂等（V15-INV-2）。
            var ser = new DataContractJsonSerializer(typeof(PlanDocument));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(NormalizeLegacyValues(json))))
            {
                return (PlanDocument)ser.ReadObject(ms);
            }
        }

        /// <summary>把 `"Value":<裸 number>` 改写为 `"Value":{"N":<裸 number>}`（仅旧形状独有形态）。
        /// 文本扫描实现；对 "Value": 前缀后跟数字起始（容空格）→ 取数字前缀包 {"N":…}，其余原样保留。
        /// 契约中 "Value" 成员只出现在 strategy/technology 的 KV 数组（P0 判据）；新形状 Value 后跟 '{' 不命中。</summary>
        public static string NormalizeLegacyValues(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            const string marker = "\"Value\":";
            var sb = new StringBuilder(json.Length + 64);
            int i = 0;
            while (true)
            {
                int hit = json.IndexOf(marker, i, StringComparison.Ordinal);
                if (hit < 0) { sb.Append(json, i, json.Length - i); break; }
                sb.Append(json, i, hit - i);
                int j = hit + marker.Length;
                while (j < json.Length && (json[j] == ' ' || json[j] == '\t')) j++;   // 容夹具空白
                if (j < json.Length && (json[j] == '-' || json[j] == '+' || json[j] == '.' || char.IsDigit(json[j])))
                {
                    int k = j;
                    while (k < json.Length && (json[k] == '-' || json[k] == '+' || json[k] == '.'
                        || json[k] == 'e' || json[k] == 'E' || char.IsDigit(json[k]))) k++;
                    sb.Append("\"Value\":{\"N\":").Append(json, j, k - j).Append('}');
                    j = k;
                }
                else
                {
                    sb.Append(marker);   // 非裸数字（新形状 {N/S} 包装）→ 原样保留
                }
                i = j;
            }
            return sb.ToString();
        }
    }
}
