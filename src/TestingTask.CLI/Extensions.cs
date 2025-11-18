namespace TestingTask.CLI;

public static class Extensions
{
    public static IEnumerable<List<T>> BatchZeroCopy<T>(this IEnumerable<T> source, int batchSize)
    {
        var batch = new List<T>(batchSize);

        foreach (var item in source)
        {
            batch.Add(item);
            if (batch.Count == batchSize)
            {
                yield return batch;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            yield return batch;
    }
}