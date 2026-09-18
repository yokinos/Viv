namespace Viv.Delusion
{
    /// <summary>
    /// 通过布尔返回值来判断结果的返回
    /// </summary>
    public interface IBooleanResult
    {
        bool IsSuccess { get; }
    }
}
