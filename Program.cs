using LunarModel.Generators;
using System;
using System.IO;

namespace LunarModel
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Generating model...");
            var fileName = @"C:\code\Stamps\Model\stamps.txt";
            var src = File.ReadAllText(fileName);

            var compiler = new Compiler();

            var modelName = Path.GetFileNameWithoutExtension(fileName).CapUpper();

            //var generator = new MemoryStore();
            var generator = new SQLStore();

            var model = compiler.Process(modelName, src, generator);

            model.Generate();
        }
    }
}
