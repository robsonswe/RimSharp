namespace RimSharp.Core.Services.Commanding
{
    /// <summary>
    /// Interface for command initializers that register commands with the command service.
    /// </summary>
    public interface ICommandInitializer
    {
        /// <summary>
        /// Initializes commands and registers them with the command service.
        /// </summary>
        /// <param name="commandService">The command service to register commands with.</param>
        void Initialize(IModCommandService commandService);
    }
}