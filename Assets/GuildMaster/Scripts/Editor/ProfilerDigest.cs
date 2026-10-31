using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Performance work (P5-2): records the main thread with the editor profiler and sums the
    /// captured frames by marker, so the cost of each system can be read from eval.
    /// <c>Start()</c>, play for a while, then <c>Report()</c>.
    /// </summary>
    public static class ProfilerDigest
    {
        public static string Start()
        {
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.enabled = true;
            Profiler.enabled = true;
            return "recording";
        }

        /// <param name="top">Rows per table.</param>
        /// <param name="slowMs">Frames longer than this also get their own average column.</param>
        /// <param name="filter">Only markers whose name contains this (null: all).</param>
        public static string Report(int top = 30, float slowMs = 16.7f, string filter = null)
        {
            ProfilerDriver.enabled = false;
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var self = new Dictionary<string, double>();
            var total = new Dictionary<string, double>();
            var selfSlow = new Dictionary<string, double>();
            var gc = new Dictionary<string, double>();
            var calls = new Dictionary<string, double>();
            var kids = new List<int>();
            var stack = new Stack<int>();
            int frames = 0, slow = 0;
            double frameSum = 0, worst = 0;
            for (int f = first; f <= last; f++)
            {
                using var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    HierarchyFrameDataView.columnTotalTime, false);
                if (v == null || !v.valid) continue;
                frames++;
                float ft = v.frameTimeMs;
                frameSum += ft;
                if (ft > worst) worst = ft;
                bool isSlow = ft > slowMs;
                if (isSlow) slow++;
                int root = v.GetRootItemID();
                stack.Clear();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    kids.Clear();
                    v.GetItemChildren(id, kids);
                    foreach (var k in kids) stack.Push(k);
                    if (id == root) continue;
                    string n = v.GetItemName(id);
                    if (filter != null && !n.Contains(filter)) continue;
                    double s = v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                    Add(self, n, s);
                    Add(total, n, v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime));
                    Add(gc, n, v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory));
                    Add(calls, n, v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls));
                    if (isSlow) Add(selfSlow, n, s);
                }
            }
            if (frames == 0) return "no frames";
            var sb = new StringBuilder();
            sb.AppendLine($"frames {frames} avg {frameSum / frames:0.00}ms worst {worst:0.0}ms slow(>{slowMs}) {slow}");
            sb.AppendLine("-- self ms/frame | slow-frame avg | total ms/frame | calls/frame");
            foreach (var kv in self.OrderByDescending(x => x.Value).Take(top))
                sb.AppendLine($"{kv.Value / frames,7:0.000} | {(slow > 0 && selfSlow.TryGetValue(kv.Key, out var q) ? q / slow : 0),6:0.00} | {total[kv.Key] / frames,7:0.000} | {calls[kv.Key] / frames,7:0.0}  {Short(kv.Key)}");
            sb.AppendLine("-- GC KB/frame");
            foreach (var kv in gc.Where(x => x.Value > 0).OrderByDescending(x => x.Value).Take(top / 2))
                sb.AppendLine($"{kv.Value / frames / 1024,7:0.00}  {Short(kv.Key)}");
            return sb.ToString();
        }

        static void Add(Dictionary<string, double> d, string k, double v) => d[k] = (d.TryGetValue(k, out var a) ? a : 0) + v;

        static string Short(string marker)
        {
            int bang = marker.IndexOf('!');
            return bang >= 0 ? marker.Substring(bang + 1).Replace(" [Invoke]", "") : marker;
        }
    }
}
