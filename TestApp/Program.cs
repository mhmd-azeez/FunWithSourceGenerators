using System;
using System.Threading.Tasks;

namespace TestApp
{
    partial class Program
    {
        static async Task Main(string[] args)
        {
           await PrintNumberAsync(42);
        }

       [Asyncify]
        static void PrintNumber(int number)
        {
            Console.WriteLine(number);
        }
    }
}
