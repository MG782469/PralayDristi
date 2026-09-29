using MediatR;

namespace PralayDrishti.Application.Features.Commands.Auth.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Unit>;
