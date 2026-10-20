namespace Viv.Herta.Core.IService
{
    public interface IPasswordHasher
    {
        /// <summary>PBKDF2-SHA256，每用户独立盐</summary>
        (string Hash, string Salt) Hash(string password);

        bool Verify(string password, string hash, string salt);
    }
}
