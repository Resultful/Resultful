namespace OneOf.ROP
{
    public interface IPlus<T, TResult>
    {
        TResult Plus(T item);
    }
}
