using System.Text;

try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch
{
    // Redirected output may not support changing the encoding.
}

return await SZA.AI.CLI.CliApplication.RunAsync(args);
