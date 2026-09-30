namespace OCUnion
{
    public static class CalcUtils
    {
        public static bool OnMidday(long tick1, long tick2) =>
            tick1 / 60000 == tick2 / 60000
                && tick1 % 60000 < 60000 / 2
                && tick2 % 60000 >= 60000 / 2;

    }
}
