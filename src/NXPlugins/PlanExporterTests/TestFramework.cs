// TestFramework.cs — 零依赖测试框架（单测骨架红线工具）
// 运行：csc 编译 src/NXPlugins/PlanExporter/*.cs + PlanExporterTests/*.cs 为单 exe，直接执行。
// 约定：测试 = public static void test_* 方法（反射枚举，异常即红）。性质编号写在方法注释。

using System;
using System.Reflection;

namespace NXPlugins.PlanExporterTests
{
    public static class Assert
    {
        public static void Fail(string msg) { throw new AssertionException(msg); }
        public static void True(bool cond, string msg)
        { if (!cond) throw new AssertionException("期望 True 失败: " + msg); }
        public static void False(bool cond, string msg)
        { if (cond) throw new AssertionException("期望 False 失败: " + msg); }
        public static void Equal(object expected, object actual, string msg)
        { if (!object.Equals(expected, actual)) throw new AssertionException(msg + "（期望=" + expected + " 实际=" + actual + "）"); }
        public static void NotNull(object o, string msg)
        { if (o == null) throw new AssertionException("期望非空: " + msg); }
        public static void Null(object o, string msg)
        { if (o != null) throw new AssertionException("期望为空: " + msg); }
        public static void Contains(string needle, string haystack, string msg)
        { if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
              throw new AssertionException(msg + "（未包含: " + needle + "）"); }
    }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string msg) : base(msg) { }
    }

    public static class Runner
    {
        public static int RunAll()
        {
            int pass = 0, fail = 0;
            Console.WriteLine("== PlanExporter 单测红线 ==");
            foreach (Type t in Assembly.GetExecutingAssembly().GetTypes())
            {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!m.Name.StartsWith("test_", StringComparison.Ordinal)) continue;
                    string name = t.Name + "." + m.Name;
                    try { m.Invoke(null, null); pass++; Console.WriteLine("  PASS " + name); }
                    catch (Exception ex)
                    {
                        fail++;
                        Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                        Console.WriteLine("  FAIL " + name + " : " + inner.Message);
                    }
                }
            }
            Console.WriteLine("== 汇总: pass=" + pass + " fail=" + fail + " ==");
            return fail;
        }

        public static int Main(string[] args)
        {
            return RunAll();
        }
    }
}
