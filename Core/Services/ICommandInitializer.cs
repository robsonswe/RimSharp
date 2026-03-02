namespace RimSharp.Core.Services.Commanding
{
    /// <summary>

    /// </summary>
    public interface ICommandInitializer
    {
        /// <summary>

        /// </summary>
        /// <param name="commandService">The command service to register commands with.</param>
        void Initialize(IModCommandService commandService);
    }
}
