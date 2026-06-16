using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Services.Dispatch;

namespace BackendApi.UnitTests.Dispatch;

public class BatchEvaluatorTests
{
    [Fact]
    public void ApplyDropoffVisitOrder_WhenSiblingVisitPositionIsEarlier_AssignsSiblingFirst()
    {
        var sibling = new Order();
        var target = new Order();

        BatchEvaluator.ApplyDropoffVisitOrder(sibling, target, [0, 3, 1, 2]);

        Assert.Equal(1, sibling.BatchSequence);
        Assert.Equal(2, target.BatchSequence);
    }

    [Fact]
    public void ApplyDropoffVisitOrder_WhenTargetVisitPositionIsEarlier_AssignsTargetFirst()
    {
        var sibling = new Order();
        var target = new Order();

        BatchEvaluator.ApplyDropoffVisitOrder(sibling, target, [0, 3, 2, 1]);

        Assert.Equal(2, sibling.BatchSequence);
        Assert.Equal(1, target.BatchSequence);
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2 })]
    [InlineData(new[] { 0, 1, 2, 2 })]
    public void ApplyDropoffVisitOrder_WhenMappingIsInvalid_UsesStableFallback(int[] mapping)
    {
        var sibling = new Order();
        var target = new Order();

        BatchEvaluator.ApplyDropoffVisitOrder(sibling, target, mapping);

        Assert.Equal(1, sibling.BatchSequence);
        Assert.Equal(2, target.BatchSequence);
    }
}


