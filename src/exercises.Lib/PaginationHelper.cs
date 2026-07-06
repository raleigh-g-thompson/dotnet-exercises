namespace exercises.Lib;

public static class PaginationHelper
{
    public static List<T> GetPage<T>(List<T> source, int pageNumber, int pageSize)
    {
        return source
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }
}
