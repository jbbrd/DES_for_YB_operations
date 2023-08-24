using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YardBlockOperations_v3
{
    class Travel
    {
        double ax, ay, az; // yard crane acceleration along all axes
        double vx, vy, vz; // yard crane speed along all axes
        double h; // yard crane height

        public Travel(double ax, double ay, double az, double vx, double vy, double vz, double h)
        {
            this.ax = ax;
            this.ay = ay;
            this.az = az;
            this.vx = vx;
            this.vy = vy;
            this.vz = vz;
            this.h = h;
        }

        public double Stack(int x0, int y0, double z0, int x1, int y1, double z1)
        {
            return horizontal_movement(x0, x1, "x") + horizontal_movement(y0, y1, "y") + 2 * vertical_movement(z0, z1);
        }

        public double Restore(int x0, int y0, int x1, int y1)
        {
            return horizontal_movement(x0, x1, "x") + horizontal_movement(y0, y1, "y");
        }

        double horizontal_movement(int origin, int destination, string axis)
        {
            double v = vy;
            double a = ay;
            double origin_m = origin * 6.546; // convert to meters, using reference slot length of 6.549m
            double destination_m = destination * 6.546;

            if (axis == "x")
            {
                v = vx;
                a = ax;
                origin_m = origin * 2.849; // convert to meters, using reference slot width of 6.549m
                destination_m = destination * 2.849;
            }



            if (Math.Abs(origin_m - destination_m) > Math.Pow(v, 2) / a)
            {
                return v / a + Math.Abs(origin_m - destination_m) / v;
            }
            else
            {
                return 2 * Math.Sqrt(Math.Abs(origin_m - destination_m) / a);
            }
        }

        double vertical_movement(double origin, double destination)
        {
            double v = vz;
            double a = az;

            if (Math.Abs(h - origin) > Math.Pow(v, 2) / a)
            {
                if (Math.Abs(destination - h) > Math.Pow(v, 2) / a)
                {
                    return 2 * v / a + Math.Abs(h - origin) / v + Math.Abs(destination - h) / v;
                }
                else
                {
                    return v / a + Math.Abs(h - origin) / v + 2 * Math.Sqrt(Math.Abs(destination - h) / a);
                }
            }
            else
            {
                if (Math.Abs(destination - h) > Math.Pow(v, 2) / a)
                {
                    return v / a + 2 * Math.Sqrt(Math.Abs(h - origin) / a) + Math.Abs(destination - h) / v;
                }
                else
                {
                    return 2 * Math.Sqrt(Math.Abs(h - origin) / a) + 2 * Math.Sqrt(Math.Abs(destination - h) / a);
                }
            }
        }
    }
}
