using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Defines a key that can be associated to a secret.
    /// </summary>
    public class SecretKeyInfo
    {
        string _secret;
        string _description;
        CKTrait _tags;
        bool _isRequired;

        /// <summary>
        /// Gets the <see cref="Tags"/>'s context.
        /// </summary>
        public static CKTraitContext TagsContext = new CKTraitContext( "SecretCategory", '|' );

        internal SecretKeyInfo( string name, Func<string, string> descriptionBuilder, bool isRequired )
            : this( name, descriptionBuilder )
        {
            _isRequired = isRequired;
        }

        internal SecretKeyInfo( string name, Func<string, string> descriptionBuilder, SecretKeyInfo subKey, IReadOnlyDictionary<string, SecretKeyInfo> keyInfos )
            : this( name, descriptionBuilder )
        {
            SubKey = subKey ?? throw new ArgumentNullException( nameof( subKey ) );
            if( subKey.SuperKey != null )
            {
                throw new ArgumentException( $"SubKey '{subKey.Name}' is already bound to SuperKey '{subKey.SuperKey.Name}'." );
            }
            if( !keyInfos.TryGetValue( subKey.Name, out var exists ) || exists != subKey )
            {
                throw new ArgumentException( $"SubKey '{subKey.Name}' is not found in store." );
            }
            subKey.SuperKey = this;
        }

        SecretKeyInfo( string name, Func<string, string> descriptionBuilder )
        {
            Name = name ?? throw new ArgumentNullException( nameof( name ) );
            SetDescription( descriptionBuilder );
        }

        internal void Reconfigure( Func<string, string> descriptionBuilder, bool isRequired )
        {
            SetDescription( descriptionBuilder );
            _isRequired |= isRequired;
            CheckSecretPropagation();
        }

        internal void Reconfigure( Func<string, string> descriptionBuilder, SecretKeyInfo subKey )
        {
            SetDescription( descriptionBuilder );
            if( subKey != SubKey )
            {
                throw new InvalidOperationException( $"Invalid SubKey specification '{subKey?.Name ?? "null"}': Key '{Name}' is bound to '{SubKey?.Name ?? "null"}'." );
            }
            CheckSecretPropagation();
        }

        void SetDescription( Func<string, string> descriptionBuilder )
        {
            if( descriptionBuilder == null ) throw new ArgumentNullException( nameof( descriptionBuilder ) );
            _description = descriptionBuilder( _description );
            if( String.IsNullOrWhiteSpace( _description ) )
            {
                throw new InvalidOperationException( $"Description must not be null or empty." );
            }
        }

        /// <summary>
        /// Gets the name of this secret key.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        public string Description => _description;

        /// <summary>
        /// Gets whether this key is required.
        /// </summary>
        public bool IsRequired => _isRequired;

        /// <summary>
        /// Gets whether this key has its associated secret available.
        /// </summary>
        public bool IsSecretAvailable => _secret != null;

        public SecretKeyInfo FinalSubKey
        {
            get
            {
                var curr = this;
                while( true )
                {
                    var previous = curr;
                    if(  curr.SubKey == null) return previous;
                    curr = curr.SubKey;
                    if( previous == curr ) throw new InvalidOperationException( "Cycle detected." );
                }
            }
        }

        [Conditional( "DEBUG" )]
        void CheckSecretPropagation()
        {
            bool isValidNull = _secret == null && (SuperKey == null || !SuperKey.IsSecretAvailable);
            bool isValidAvailable = _secret != null && (SuperKey == null || SuperKey._secret == _secret);
            Debug.Assert( isValidNull || isValidAvailable );
        }

        /// <summary>
        /// Gets the secret or null if <see cref="IsSecretAvailable"/> is false).
        /// (Note that this secret may be defined by the <see cref="SuperKey"/>.)
        /// </summary>
        public string Secret => _secret;


        /// <summary>
        /// Sets the secret for this key an its <see cref="SubKey"/> (recursively) if any.
        /// If the current secret is provided by the <see cref="SuperKey"/>, this
        /// raises an <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <param name="secret">The secret to set. Can be null to clear it.</param>
        public void SetSecret( string secret )
        {
            CheckSecretPropagation();
            if( SuperKey != null && SuperKey.IsSecretAvailable ) throw new InvalidOperationException( $"Secret is defined by the SuperKey '{SuperKey.Name}'." );
            SecretKeyInfo k = this;
            do
            {
                _secret = secret;
                k = k.SubKey;
            }
            while( k != null );
        }

        /// <summary>
        /// Gets or sets one or more tags. When set to null, tags is set to the <see cref="CKTraitContext.EmptyTrait"/>.
        /// When not null, tags must belong to <see cref="TagsContext"/>
        /// </summary>
        public CKTrait Tags
        {
            get => _tags;
            set
            {
                if( value == null ) _tags = TagsContext.EmptyTrait;
                else if( value.Context != TagsContext ) throw new ArgumentException( "Tag context mismatch." );
                _tags = value;
            }
        }

        /// <summary>
        /// Gets the secret key that is more powerful than this one or null if no such key exists.
        /// This super key if it exists and if its <see cref="IsSecretAvailable"/> is true, overrides the secret at this level.
        /// </summary>
        public SecretKeyInfo SuperKey { get; private set; }

        /// <summary>
        /// Gets the secret key that is covered by this one or null if no such key exists.
        /// This subordinated key's secret is replaced by this one if it is set.
        /// </summary>
        public SecretKeyInfo SubKey { get; }

        /// <summary>
        /// Gets the <see cref="Name"/>, <see cref="Description"/>, <see cref="IsRequired"/> and <see cref="IsSecretAvailable"/> status.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"Secret '{Name}': {_description} [{(IsRequired ? "Required" : "Optional")},{(IsSecretAvailable ? "Available" : "Unavailable")}]";

    }
}
