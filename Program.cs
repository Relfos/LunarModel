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
            var fileName = "stamps.txt";
            var src = File.ReadAllText(fileName);

            var compiler = new Compiler();

            var modelName = Path.GetFileNameWithoutExtension(fileName).CapUpper();

            var model = compiler.Process(modelName, src, new MemoryStore());

            model.Generate();
        }
    }
}
