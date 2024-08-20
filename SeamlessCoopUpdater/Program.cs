using System;
using System.Threading.Tasks;
using SeamlessCoopUpdater.Logic;
using SeamlessCoopUpdater.Models;

namespace SeamlessCoopUpdater;

class Program
{
    
    static async Task Main()
    {
        try
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Console.WriteLine("Ce programme ne fonctionne que sous Windows.");
                Environment.Exit(-1);
            }
            
            Updater updater = new();

            UpdatesStatus updatesStatus = await updater.CheckForUpdates();
            
            if (updatesStatus == UpdatesStatus.Error)
            {
                Console.WriteLine("Une erreur est survenue lors de la vérification des mises à jour.");
                Console.WriteLine("Appuyez sur une touche pour quitter.");
                Console.ReadKey();
                Environment.Exit(-1);
            }

            if (updatesStatus == UpdatesStatus.UpToDate)
            {
                Console.WriteLine("Le mod est déjà à jour.");
                Console.WriteLine("Voulez-vous forcer la mise à jour ? ([o]/n)");
                var forceUpdate = Console.ReadKey();
                if (forceUpdate.Key != ConsoleKey.Enter && forceUpdate.KeyChar != 'O' && forceUpdate.KeyChar != 'o' && forceUpdate.KeyChar != 'Y' && forceUpdate.KeyChar != 'y')
                {
                    Environment.Exit(0);
                }
            }

            updater.Update();

            Console.WriteLine("Appuyez sur une touche pour quitter.");
            Console.ReadKey();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.WriteLine("Appuyez sur une touche pour quitter.");
            Console.ReadKey();
        }
    }
}