using Griffin.Networking.Messages;

namespace Griffin.Networking
{
    /// <summary>
    /// A channel is a device used for the IO operations
    /// </summary>
    /// <remarks>
    /// <para>Each channel should be able to handle <see cref="Close"/>, <see cref="Connect"/>, <see cref="SendMessage"/></para>
    /// </remarks>
    public interface IChannel
    {
        /// <summary>
        /// A message have been sent through the pipeline and are ready to be handled by the channel.
        /// </summary>
        /// <param name="message">Message that the channel should process.</param>
        void HandleDownstream(IPipelineMessage message);
    }
}