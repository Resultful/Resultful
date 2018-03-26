using System;

namespace OneOf.ROP.Utils
{
    internal static class TupleHelper
    {
        public static(T0, T1, T2)
            Unfold<T0, T1, T2>
                (this
                    ((T0, T1), T2)
                    item)
        {
            var (tuple, item2) = item;
            var (item0, item1) = tuple;
            return (item0, item1, item2);
        }

        public static(T0, T1, T2, T3)
            Unfold<T0, T1, T2, T3>
                (this
                    ((T0, T1, T2), T3)
                    item)
        {
            var (tuple, item3) = item;
            var (item0, item1, item2) = tuple;
            return (item0, item1, item2, item3);
        }

        public static(T0, T1, T2, T3, T4)
            Unfold<T0, T1, T2, T3, T4>
                (this
                    ((T0, T1, T2, T3), T4)
                    item)
        {
            var (tuple, item4) = item;
            var (item0, item1, item2, item3) = tuple;
            return (item0, item1, item2, item3, item4);
        }

        public static(T0, T1, T2, T3, T4, T5)
            Unfold<T0, T1, T2, T3, T4, T5>
                (this
                    ((T0, T1, T2, T3, T4), T5)
                    item)
        {
            var (tuple, item5) = item;
            var (item0, item1, item2, item3, item4) = tuple;
            return (item0, item1, item2, item3, item4, item5);
        }

        public static(T0, T1, T2, T3, T4, T5, T6)
            Unfold<T0, T1, T2, T3, T4, T5, T6>
                (this
                    ((T0, T1, T2, T3, T4, T5), T6)
                    item)
        {
            var (tuple, item6) = item;
            var (item0, item1, item2, item3, item4, item5) = tuple;
            return (item0, item1, item2, item3, item4, item5, item6);
        }

        public static(T0, T1, T2, T3, T4, T5, T6, T7)
            Unfold<T0, T1, T2, T3, T4, T5, T6, T7>
                (this
                    ((T0, T1, T2, T3, T4, T5, T6), T7)
                    item)
        {
            var (tuple, item7) = item;
            var (item0, item1, item2, item3, item4, item5, item6) = tuple;
            return (item0, item1, item2, item3, item4, item5, item6, item7);
        }

    }
}
