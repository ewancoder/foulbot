using System.Reflection;
using AutoFixture.Kernel;

namespace FoulBot.Domain.Tests;

public abstract class CustomParameterBuilder<TObject, TParameter>(
    string parameterName, TParameter parameterValue)
    : ISpecimenBuilder
{
    public object? Create(object request, ISpecimenContext context)
    {
        if (request is not ParameterInfo pi)
            return new NoSpecimen();

        if (pi.Member.DeclaringType != typeof(TObject) ||
            pi.ParameterType != typeof(TParameter) ||
            pi.Name != parameterName)
            return new NoSpecimen();

        return parameterValue;
    }
}
