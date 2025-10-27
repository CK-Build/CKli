using CK.Core;
using System;

namespace CKli.Core;

public partial class TableLayout
{
    sealed class ColDef
    {
        readonly ColumnDefinition? _def;
        int _minW;
        int _maxW;
        int _nominalWidth;
        int _maxInitialWidth;
        int _maxNominalWidth;
        int _width;

        public ColDef()
        {
        }

        public ColDef( ColumnDefinition? def )
        {
            _def = def;
            if( def != null )
            {
                _minW = def.MinWidth;
                _maxW = def.MaxWidth;
            }
        }

        public ColDef( IRenderable r, bool addCollapsableWidth )
        {
            _minW = r.MinWidth;
            _maxInitialWidth = r.Width;
            _maxNominalWidth = r.NominalWidth;
            if( addCollapsableWidth )
            {
                _minW += 2;
                _maxInitialWidth += 2;
                _maxNominalWidth += 2;
            }
        }

        public int MinW => _minW;

        public int MaxW => _maxW;

        public bool IsEmpty => _minW == 0 && _maxInitialWidth == 0;

        public int Width => _width;

        internal void Add( IRenderable r, bool addCollapsableWidth )
        {
            var mW = r.MinWidth;
            if( addCollapsableWidth ) mW += 2;
            if( _minW < mW )
            {
                _minW = mW;
                if( _maxW != 0 && _maxW < mW ) _maxW = mW;
            }
            int w = r.Width;
            if( addCollapsableWidth ) w += 2;
            if( _maxInitialWidth < w ) _maxInitialWidth = w;
            w = r.NominalWidth;
            if( _maxNominalWidth < w ) _maxNominalWidth = w;
        }

        // For column 0 in a Collapsable before applying widths.
        internal void HideCollapsableWidth() => _width -= 2;

        // For column 0 in a Collapsable after applying widths.
        internal void RestoreCollapsableWidth() => _width += 2;

        // After discovery on the last column when all columns have a max width.
        internal void ClearMaxWidth() => _maxW = 0;

        internal void SettleWidths( out int minWidth, out int nominalWidth, out int width )
        {
            Throw.DebugAssert( _width == 0 );
            minWidth = _minW;
            if( _maxW != 0 )
            {
                _width = width = int.Clamp( _maxInitialWidth, _minW, _maxW );
                _nominalWidth = nominalWidth = int.Clamp( _maxNominalWidth, _minW, _maxW );
            }
            else
            {
                _width = width = Math.Max( _maxInitialWidth, _minW );
                _nominalWidth = nominalWidth = Math.Max( _maxNominalWidth, _minW );
            }
        }

        ColDef Clone() => (ColDef)MemberwiseClone();

        ColDef CloneMin()
        {
            var c = Clone();
            c._width = _minW;
            return c;
        }

        ColDef CloneNominal()
        {
            var c = Clone();
            c._width = _nominalWidth;
            return c;
        }

        internal static ColDef[] ToMinWidth( ColDef[] defs )
        {
            var r = new ColDef[ defs.Length ];
            for( int i = 0; i < defs.Length; i++ )
            {
                r[i] = defs[i].CloneMin();
            }
            return r;
        }

        internal static ColDef[] ToNominalWidth( ColDef[] defs )
        {
            var r = new ColDef[ defs.Length ];
            for( int i = 0; i < defs.Length; i++ )
            {
                r[i] = defs[i].CloneNominal();
            }
            return r;
        }

        internal static ColDef[] ToWidth( ColDef[] defs, int nominalWidth, int width )
        {
            var cols = new ColDef[defs.Length];
            for( int i = 0; i < defs.Length; i++ )
            {
                cols[i] = defs[i].Clone();
            }
            double ratio = (double)width / nominalWidth;
            int sum = 0;
            for( int i = 0; i < cols.Length; i++ )
            {
                var c = cols[i];
                int w = Math.Max( (int)Math.Round( (ratio * c._nominalWidth) + 0.5, MidpointRounding.ToZero ), c._minW );
                if( c._maxW != 0 && w > c._maxW ) w = c._maxW;
                c._width = w;
                sum += w;
            }
            int delta = width - sum;
            if( delta > 0 )
            {
                do
                {
                    for( int i = cols.Length - 1; i >= 0; i-- )
                    {
                        var c = cols[i];
                        if( c._maxW == 0 || c._width < c._maxW )
                        {
                            c._width++;
                            if( --delta == 0 ) break;
                        }
                    }
                }
                while( delta != 0 );
            }
            else if( delta < 0 )
            {
                do
                {
                    for( int i = cols.Length - 1; i >= 0; i-- )
                    {
                        var c = cols[i];
                        if( c._width > c._minW )
                        {
                            c._width--;
                            if( ++delta == 0 ) break;
                        }
                    }
                }
                while( delta != 0 );
            }
            return cols;
        }
    }

}
