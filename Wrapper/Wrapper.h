#pragma once
#include <time.h>
#include <stdlib.h>

public ref class Wrapper
{
public:
	static long long Time()
	{
		return time(0);
	}

	static int TimeCast()
	{
		return time(0);
	}

	static void SRand(unsigned int s)
	{
		srand(s);
	}

	static int Rand()
	{
		return rand();
	}

	static int RandMax()
	{
		return RAND_MAX;
	}
};