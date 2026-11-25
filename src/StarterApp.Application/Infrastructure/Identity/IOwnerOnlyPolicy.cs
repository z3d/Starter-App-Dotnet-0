namespace StarterApp.Application.Infrastructure.Identity;

public interface IOwnerOnlyPolicy
{
    OwnerScope GetRequiredScope();
    void Authorize(string ownerSubject, string tenantId);
}
