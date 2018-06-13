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
            var ((item0, item1), item2) = item;
            return (item0, item1, item2);
        }

        public static(T0, T1, T2)
            Unfold<T0, T1, T2>
                (this
                    (T0, (T1, T2))
                    item)
        {
            var (item0, (item1, item2)) = item;
            return (item0, item1, item2);
        }

        public static(T0, T1, T2, T3)
            Unfold<T0, T1, T2, T3>
                (this
                    ((T0, T1, T2), T3)
                    item)
        {
            var ((item0, item1, item2), item3) = item;
            return (item0, item1, item2, item3);
        }

        public static(T0, T1, T2, T3)
            Unfold<T0, T1, T2, T3>
                (this
                    (T0, (T1, T2, T3))
                    item)
        {
            var (item0, (item1, item2, item3)) = item;
            return (item0, item1, item2, item3);
        }

        public static(T0, T1, T2, T3)
            Unfold<T0, T1, T2, T3>
                (this
                    ((T0, T1), (T2, T3))
                    item)
        {
            var ((item0, item1), (item2, item3)) = item;
            return (item0, item1, item2, item3);
        }

        public static(T0, T1, T2, T3, T4)
            Unfold<T0, T1, T2, T3, T4>
                (this
                    ((T0, T1, T2, T3), T4)
                    item)
        {
            var ((item0, item1, item2, item3), item4) = item;
            return (item0, item1, item2, item3, item4);
        }

        public static(T0, T1, T2, T3, T4)
            Unfold<T0, T1, T2, T3, T4>
                (this
                    (T0, (T1, T2, T3, T4))
                    item)
        {
            var (item0, (item1, item2, item3, item4)) = item;
            return (item0, item1, item2, item3, item4);
        }

        public static(T0, T1, T2, T3, T4)
            Unfold<T0, T1, T2, T3, T4>
                (this
                    ((T0, T1, T2), (T3, T4))
                    item)
        {
            var ((item0, item1, item2), (item3, item4)) = item;
            return (item0, item1, item2, item3, item4);
        }

        public static(T0, T1, T2, T3, T4)
            Unfold<T0, T1, T2, T3, T4>
                (this
                    ((T0, T1), (T2, T3, T4))
                    item)
        {
            var ((item0, item1), (item2, item3, item4)) = item;
            return (item0, item1, item2, item3, item4);
        }

        public static(T0, T1, T2, T3, T4, T5)
            Unfold<T0, T1, T2, T3, T4, T5>
                (this
                    ((T0, T1, T2, T3, T4), T5)
                    item)
        {
            var ((item0, item1, item2, item3, item4), item5) = item;
            return (item0, item1, item2, item3, item4, item5);
        }

        public static(T0, T1, T2, T3, T4, T5)
            Unfold<T0, T1, T2, T3, T4, T5>
                (this
                    (T0, (T1, T2, T3, T4, T5))
                    item)
        {
            var (item0, (item1, item2, item3, item4, item5)) = item;
            return (item0, item1, item2, item3, item4, item5);
        }

        public static(T0, T1, T2, T3, T4, T5)
            Unfold<T0, T1, T2, T3, T4, T5>
                (this
                    ((T0, T1, T2, T3), (T4, T5))
                    item)
        {
            var ((item0, item1, item2, item3), (item4, item5)) = item;
            return (item0, item1, item2, item3, item4, item5);
        }

        public static(T0, T1, T2, T3, T4, T5)
            Unfold<T0, T1, T2, T3, T4, T5>
                (this
                    ((T0, T1), (T2, T3, T4, T5))
                    item)
        {
            var ((item0, item1), (item2, item3, item4, item5)) = item;
            return (item0, item1, item2, item3, item4, item5);
        }

        public static(T0, T1, T2, T3, T4, T5)
            Unfold<T0, T1, T2, T3, T4, T5>
                (this
                    ((T0, T1, T2), (T3, T4, T5))
                    item)
        {
            var ((item0, item1, item2), (item3, item4, item5)) = item;
            return (item0, item1, item2, item3, item4, item5);
        }

        public static(T0, T1, T2, T3, T4, T5, T6)
            Unfold<T0, T1, T2, T3, T4, T5, T6>
                (this
                    ((T0, T1, T2, T3, T4, T5), T6)
                    item)
        {
            var ((item0, item1, item2, item3, item4, item5), item6) = item;
            return (item0, item1, item2, item3, item4, item5, item6);
        }

        public static(T0, T1, T2, T3, T4, T5, T6)
            Unfold<T0, T1, T2, T3, T4, T5, T6>
                (this
                    (T0, (T1, T2, T3, T4, T5, T6))
                    item)
        {
            var (item0, (item1, item2, item3, item4, item5, item6)) = item;
            return (item0, item1, item2, item3, item4, item5, item6);
        }

        public static(T0, T1, T2, T3, T4, T5, T6)
            Unfold<T0, T1, T2, T3, T4, T5, T6>
                (this
                    ((T0, T1, T2, T3, T4), (T5, T6))
                    item)
        {
            var ((item0, item1, item2, item3, item4), (item5, item6)) = item;
            return (item0, item1, item2, item3, item4, item5, item6);
        }

        public static(T0, T1, T2, T3, T4, T5, T6)
            Unfold<T0, T1, T2, T3, T4, T5, T6>
                (this
                    ((T0, T1), (T2, T3, T4, T5, T6))
                    item)
        {
            var ((item0, item1), (item2, item3, item4, item5, item6)) = item;
            return (item0, item1, item2, item3, item4, item5, item6);
        }

        public static(T0, T1, T2, T3, T4, T5, T6)
            Unfold<T0, T1, T2, T3, T4, T5, T6>
                (this
                    ((T0, T1, T2, T3), (T4, T5, T6))
                    item)
        {
            var ((item0, item1, item2, item3), (item4, item5, item6)) = item;
            return (item0, item1, item2, item3, item4, item5, item6);
        }

        public static(T0, T1, T2, T3, T4, T5, T6)
            Unfold<T0, T1, T2, T3, T4, T5, T6>
                (this
                    ((T0, T1, T2), (T3, T4, T5, T6))
                    item)
        {
            var ((item0, item1, item2), (item3, item4, item5, item6)) = item;
            return (item0, item1, item2, item3, item4, item5, item6);
        }

        public static(T0, T1, T2, T3, T4, T5, T6, T7)
            Unfold<T0, T1, T2, T3, T4, T5, T6, T7>
                (this
                    ((T0, T1, T2, T3, T4, T5, T6), T7)
                    item)
        {
            var ((item0, item1, item2, item3, item4, item5, item6), item7) = item;
            return (item0, item1, item2, item3, item4, item5, item6, item7);
        }

        public static(T0, T1, T2, T3, T4, T5, T6, T7)
            Unfold<T0, T1, T2, T3, T4, T5, T6, T7>
                (this
                    (T0, (T1, T2, T3, T4, T5, T6, T7))
                    item)
        {
            var (item0, (item1, item2, item3, item4, item5, item6, item7)) = item;
            return (item0, item1, item2, item3, item4, item5, item6, item7);
        }

        public static(T0, T1, T2, T3, T4, T5, T6, T7)
            Unfold<T0, T1, T2, T3, T4, T5, T6, T7>
                (this
                    ((T0, T1, T2, T3, T4, T5), (T6, T7))
                    item)
        {
            var ((item0, item1, item2, item3, item4, item5), (item6, item7)) = item;
            return (item0, item1, item2, item3, item4, item5, item6, item7);
        }

        public static(T0, T1, T2, T3, T4, T5, T6, T7)
            Unfold<T0, T1, T2, T3, T4, T5, T6, T7>
                (this
                    ((T0, T1), (T2, T3, T4, T5, T6, T7))
                    item)
        {
            var ((item0, item1), (item2, item3, item4, item5, item6, item7)) = item;
            return (item0, item1, item2, item3, item4, item5, item6, item7);
        }

        public static(T0, T1, T2, T3, T4, T5, T6, T7)
            Unfold<T0, T1, T2, T3, T4, T5, T6, T7>
                (this
                    ((T0, T1, T2, T3, T4), (T5, T6, T7))
                    item)
        {
            var ((item0, item1, item2, item3, item4), (item5, item6, item7)) = item;
            return (item0, item1, item2, item3, item4, item5, item6, item7);
        }

        public static(T0, T1, T2, T3, T4, T5, T6, T7)
            Unfold<T0, T1, T2, T3, T4, T5, T6, T7>
                (this
                    ((T0, T1, T2), (T3, T4, T5, T6, T7))
                    item)
        {
            var ((item0, item1, item2), (item3, item4, item5, item6, item7)) = item;
            return (item0, item1, item2, item3, item4, item5, item6, item7);
        }

        public static(T0, T1, T2, T3, T4, T5, T6, T7)
            Unfold<T0, T1, T2, T3, T4, T5, T6, T7>
                (this
                    ((T0, T1, T2, T3), (T4, T5, T6, T7))
                    item)
        {
            var ((item0, item1, item2, item3), (item4, item5, item6, item7)) = item;
            return (item0, item1, item2, item3, item4, item5, item6, item7);
        }

    }
}
