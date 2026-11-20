namespace Resultful
{
    public interface IPlus<in T, out TResult>
    {
        TResult Plus(T item);
    }
}
