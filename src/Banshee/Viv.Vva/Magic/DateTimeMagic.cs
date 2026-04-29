using System.Diagnostics.CodeAnalysis;

namespace Viv.Vva.Magic
{
    public class DateTimeMagic
    {
        /// <summary>
        /// 获取年龄
        /// </summary>
        /// <param name="inputBirthDay"></param>
        /// <returns></returns>
        [return: MaybeNull]
        public static AgeView? GetAge(DateTime? inputBirthDay)
        {
            if (inputBirthDay == null) return null;

            var birthDay = inputBirthDay.Value;
            var now = DateTime.Now;
            int years = now.Year - birthDay.Year;
            if (now.Month < birthDay.Month || (now.Month == birthDay.Month && now.Day < birthDay.Day))
                years--;

            var temp = birthDay.AddYears(years);
            int months = 0;
            while (temp.AddMonths(1) <= now)
            {
                months++;
                temp = temp.AddMonths(1);
            }

            int days = (int)(now - temp).TotalDays;
            return new AgeView { Years = years, Months = months, Days = days };
        }
    }

    public class AgeView
    {
        public int Years { get; set; }
        public int Months { get; set; }
        public int Days { get; set; }
        public override string ToString()
        {
            return $"{Years}岁{Months}月{Days}天";
        }
    }
}
