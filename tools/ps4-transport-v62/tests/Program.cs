using System;
using Tayson.Providers;

static class Program
{
    static int count;
    static void Assert(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL " + name);
        ++count;
        Console.WriteLine("PASS " + name);
    }
    static void Good(string address, string expected)
    {
        bool valid = Ps4LanAddressPolicy.TryNormalize(address, out var value);
        Assert(valid && value == expected, "valid: " + address);
    }
    static void Bad(string address)
    {
        bool valid = Ps4LanAddressPolicy.TryNormalize(address, out var value);
        Assert(!valid && value.Length == 0, "rejected: " + address);
    }

    static void Main()
    {
        Good("192.168.0.7","192.168.0.7");
        Good("192.168.0.5","192.168.0.5");
        Good(" 192.168.1.10 ","192.168.1.10");
        Good("10.0.0.1","10.0.0.1");
        Good("10.255.255.254","10.255.255.254");
        Good("172.16.0.10","172.16.0.10");
        Good("172.31.254.254","172.31.254.254");
        Good("192.168.255.254","192.168.255.254");

        Bad(""); Bad(" ");
        Bad("localhost"); Bad("ps4.home");
        Bad("8.8.8.8"); Bad("1.1.1.1");
        Bad("127.0.0.1"); Bad("0.0.0.0");
        Bad("169.254.1.7"); Bad("224.0.0.1");
        Bad("192.168.0.0"); Bad("192.168.0.255");
        Bad("192.168.00.7"); Bad("192.168.0.007");
        Bad("192.168.0.256"); Bad("192.168.1");
        Bad("192.168.0.1:9090"); Bad("http://192.168.0.7");
        Bad("172.15.0.3"); Bad("172.32.0.3");
        Bad("fe80::1"); Bad("::ffff:192.168.0.7");
        Bad("192.168.0.7/24"); Bad("192.168.0.-1");
        Assert(Ps4LanAddressPolicy.BinLoaderPort == 9090, "standard BinLoader port");
        Console.WriteLine("RESULT: "+count+" transport safety checks passed");
    }
}