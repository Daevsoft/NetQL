using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestDemo.Utils
{
    class Randomize
    {
        private static readonly Random Random = new Random();

        public static List<int> GenerateRandom(int count, int min, int max)
        {
            if (max <= min || count < 0 ||
                (count > max - min && max - min > 0))
            {
                throw new ArgumentOutOfRangeException("Range " + min + " to " + max +
                                                      " (" + (max - (long)min) + " values), or count " + count +
                                                      " is illegal");
            }
            var candidates = new HashSet<int>();

            for (var top = max - count; top < max; top++)
            {
                if (!candidates.Add(Random.Next(min, top + 1)))
                {
                    candidates.Add(top);
                }
            }
            var result = candidates.ToList();
            for (var i = result.Count - 1; i > 0; i--)
            {
                var k = Random.Next(i + 1);
                var tmp = result[k];
                result[k] = result[i];
                result[i] = tmp;
            }
            return result;
        }

        public static List<int> GenerateRandom(int count)
        {
            return GenerateRandom(count, 0, int.MaxValue);
        }
    }
}
