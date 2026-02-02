using CK.Core;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.GitHosting.Providers;

public abstract partial class HttpGitHostingProvider
{
    /// <summary>
    /// Optional helper that can be specialized or uses as-is in <see cref="OnSendHookAsync"/>.
    /// </summary>
    protected class RetryHelper
    {
        TimeSpan _currentRegularRetryDelay;
        int _regularRetryCount;
        int _maxRegularRetryCount;
        int _overallRetryCount;

        /// <summary>
        /// Initializes a new retry helper.
        /// </summary>
        /// <param name="baseRegularRetryDelayMS">Base delay for regular retries.</param>
        /// <param name="maxRegularRetryCount">Maximal number of regular retries.</param>
        public RetryHelper( int baseRegularRetryDelayMS = 200, int maxRegularRetryCount = 5 )
        {
            Throw.CheckArgument( baseRegularRetryDelayMS > 50 );
            _currentRegularRetryDelay = TimeSpan.FromMilliseconds( baseRegularRetryDelayMS );
            _maxRegularRetryCount = maxRegularRetryCount;
        }

        /// <summary>
        /// Gets the total number of retries.
        /// </summary>
        public int OverallRetryCount => _overallRetryCount;

        /// <summary>
        /// Gets or sets the maximal regular retry count.
        /// </summary>
        public int MaxRegularRetryCount
        {
            get => _maxRegularRetryCount;
            set => _maxRegularRetryCount = value;
        }

        /// <summary>
        /// Gets whether this response must be considered successful.
        /// Returns <see cref="HttpResponseMessage.IsSuccessStatusCode"/> by default.
        /// </summary>
        /// <param name="response">The response message.</param>
        /// <returns>True if this is a successful response, false otherwise.</returns>
        public virtual bool IsSuccessfulResponse( HttpResponseMessage response )
        {
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// By default:
        /// <list type="number">
        ///   <item>Always honor Retry-After header if present (calls <see cref="UseRetryAfter"/>).</item>
        ///   <item>Otherwise calls <see cref="FinalUseRegularNextDelay(IActivityMonitor, HttpResponseMessage)"/>.</item>
        /// </list>
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="response">The unsuccessful response.</param>
        /// <returns>The delay to wait, null otherwise.</returns>
        public virtual TimeSpan? OnUnsuccessfulResponse( IActivityMonitor monitor, HttpResponseMessage response )
        {
            Throw.CheckArgument( !response.IsSuccessStatusCode );
            return UseRetryAfter( monitor, response )
                   ?? FinalUseRegularNextDelay( monitor, response );
        }

        /// <summary>
        /// Returns the Retry-After header value. When the header exists, this is the definitive
        /// new delay to use so this logs a warning that explains the retry.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="response">The unsuccessful response.</param>
        /// <returns>The delay to wait, null otherwise.</returns>
        protected TimeSpan? UseRetryAfter( IActivityMonitor monitor, HttpResponseMessage response )
        {
            var retryAfterDelay = GetRetryAfter( response );
            if( retryAfterDelay is not null )
            {
                ++_overallRetryCount;
                monitor.Warn( $"Received '{response.StatusCode}' from '{response.RequestMessage?.RequestUri}'. Waiting for '{retryAfterDelay}' based on retry-after header." );
                return retryAfterDelay;
            }
            return null;

            static TimeSpan? GetRetryAfter( HttpResponseMessage response ) => response.Headers.RetryAfter switch
            {
                { Date: { } date } => date - DateTimeOffset.UtcNow,
                { Delta: { } delta } => delta,
                _ => null,
            };
        }

        /// <summary>
        /// Returns the next regular delay to wait or null if retrying is over.
        /// </summary>
        /// <returns>The delay to wait, null otherwise.</returns>
        protected virtual TimeSpan? UseRegularNextDelay()
        {
            if( ++_regularRetryCount <= _maxRegularRetryCount )
            {
                ++_overallRetryCount;
                _currentRegularRetryDelay *= 2;
                return _currentRegularRetryDelay;
            }
            return null;
        }

        /// <summary>
        /// Helper that calls <see cref="UseRegularNextDelay"/> and logs a warning or
        /// calls <see cref="OnGiveUp(IActivityMonitor, Exception)"/>.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="ex">The exception.</param>
        /// <returns>The delay to wait, null otherwise.</returns>
        protected virtual TimeSpan? FinalUseRegularNextDelay( IActivityMonitor monitor, Exception ex )
        {
            var delay = UseRegularNextDelay();
            if( delay > TimeSpan.Zero )
            {
                monitor.Warn( $"Retrying in '{delay}'.", ex );
            }
            else
            {
                OnGiveUp( monitor, ex );
            }
            return delay;
        }

        /// <summary>
        /// Helper that calls <see cref="UseRegularNextDelay"/> and logs a warning or calls <see cref="OnGiveUp(IActivityMonitor, HttpResponseMessage)"/>.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="response">The unsuccessful response.</param>
        /// <returns>The delay to wait, null otherwise.</returns>
        protected virtual TimeSpan? FinalUseRegularNextDelay( IActivityMonitor monitor, HttpResponseMessage response )
        {
            var delay = UseRegularNextDelay();
            if( delay > TimeSpan.Zero )
            {
                monitor.Warn( $"Received '{response.StatusCode}' from '{response.RequestMessage?.RequestUri}'. Retrying in '{delay}'." );
            }
            else
            {
                OnGiveUp( monitor, response );
            }
            return delay;
        }

        /// <summary>
        /// Called when <see cref="MaxRegularRetryCount"/> has been reached.
        /// Logs the final error message.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="response">The unsuccessful response.</param>
        protected virtual void OnGiveUp( IActivityMonitor monitor, HttpResponseMessage response )
        {
            monitor.Error( $"Received '{response.StatusCode}' from '{response.RequestMessage?.RequestUri}'. Giving up after {OverallRetryCount} retries." );
        }

        /// <summary>
        /// Called when <see cref="MaxRegularRetryCount"/> has been reached.
        /// Logs the final error message.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="ex">The exception.</param>
        protected virtual void OnGiveUp( IActivityMonitor monitor, Exception ex )
        {
            monitor.Error( $"Giving up after {OverallRetryCount} retries.", ex );
        }
    }
}
