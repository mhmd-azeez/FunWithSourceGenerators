using System;
using System.Threading.Tasks;

namespace TestApp
{
    partial class Program
    {
        static async Task Main(string[] args)
        {
           await SomeMethodAsync(42);
        }

        [Asyncify]
        static void SomeMethod(int number)
        {
            Console.WriteLine(number);
        }
    }
}
