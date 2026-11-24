namespace S7.NetCore.Internal;

/// <summary>
/// 批量写条目目标字节区间重叠检测。位条目按所在字节参与。
/// </summary>
internal static class WriteOverlapDetector
{
    internal static void Validate(IReadOnlyList<BatchWireItem> items)
    {
        var groups = new Dictionary<(S7Area Area, int DbNumber), List<(int Index, int Start, int End)>>();
        for (int i = 0; i < items.Count; i++)
        {
            BatchWireItem item = items[i];
            var key = (item.Area, item.DbNumber);
            if (!groups.TryGetValue(key, out List<(int Index, int Start, int End)>? ranges))
            {
                ranges = new List<(int Index, int Start, int End)>();
                groups.Add(key, ranges);
            }

            int length = Math.Max(1, item.Length);
            ranges.Add((i, item.StartByte, item.StartByte + length));
        }

        foreach (List<(int Index, int Start, int End)> ranges in groups.Values)
        {
            ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            int previousIndex = -1;
            int previousEnd = -1;
            foreach ((int index, int start, int end) in ranges)
            {
                if (start < previousEnd)
                {
                    throw new S7ClientException(
                        S7ErrorCategory.InvalidArgument,
                        S7ErrorCode.BatchAddressConflict,
                        S7OperationStage.Validate,
                        $"批量写条目地址重叠：条目 {previousIndex} 与条目 {index} 的目标字节区间相交。");
                }

                previousIndex = index;
                previousEnd = end;
            }
        }
    }
}