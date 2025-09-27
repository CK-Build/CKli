//using ConsoleAppFramework;
//using System;

//namespace CKli;

//[RegisterCommands( "layout" )]
//public sealed class LayoutCommands
//{
//    /// <summary>
//    /// Fixes the the folders and repositories layout of the current world. 
//    /// </summary>
//    /// <param name="deleteAliens">Delete repositories that don't belong to the current world.</param>
//    /// <returns>0 on success, negative on error.</returns>
//    public int Fix( bool deleteAliens = false )
//    {
//        return CommandContext.Run( ( monitor, userPreferences ) =>
//        {
//            return CKliCommands.LayoutFix( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, deleteAliens );
//        } );
//    }

//    /// <summary>
//    /// Updates the layout of the current world from existing folders and repositories.
//    ///             To share this updated layout with others, 'push --stackOnly' must be executed. 
//    /// </summary>
//    /// <returns>0 on success, negative on error.</returns>
//    public int Xif()
//    {
//        return CommandContext.Run( ( monitor, userPreferences ) =>
//        {
//            return CKliCommands.LayoutXif( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory );
//        } );
//    }

//}
