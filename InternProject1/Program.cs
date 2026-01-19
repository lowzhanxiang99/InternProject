using System;

namespace MyFirstApp
{
    class Program
    {
        static void Main(string[] args)
        {
            // 1. 打印欢迎语
            Console.WriteLine("=== Welcome to my  C# practical ===");

            // 2. 获取用户输入
            Console.Write("Enter your name: ");
            string name = Console.ReadLine();

            Console.Write("Enter year of birthday (example 1999): ");
            string inputYear = Console.ReadLine();

            // 3. 简单的逻辑计算 (计算年龄)
            int birthYear = int.Parse(inputYear);
            int currentYear = DateTime.Now.Year;
            int age = currentYear - birthYear;

            // 4. 输出结果
            Console.WriteLine($"\nHii, {name}!");
            Console.WriteLine($"Based of calculation, this year you are {age} age。");

            // 5. 防止程序立刻关闭
            Console.WriteLine("\nPlease any key to exit...");
            Console.ReadKey();
        }
    }
}