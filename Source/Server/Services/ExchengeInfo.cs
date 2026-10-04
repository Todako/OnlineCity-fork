using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class ExchengeInfo : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request53ExchengeInfo;

        public int ResponseTypePackage => (int)PackageType.Response54ExchengeInfo;

        private static readonly ModelExchengeInfo ErrorResponse = new ModelExchengeInfo
        {
            Status = 1,
            Message = null,
            Result = 0
        };

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;

            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = exchengeInfo(request.Packet as ModelExchengeInfo, context);
            return result;
        }

        private ModelExchengeInfo exchengeInfo(ModelExchengeInfo request, ServiceContext context)
        {
            if (request == null || context?.Player == null)
            {
                return ErrorResponse;
            }

            try
            {
                lock (context.Player)
                {
                    var data = Repository.GetData;
                    if (data?.OrderOperator == null)
                    {
                        return ErrorResponse;
                    }

                    lock (data)
                    {
                        switch (request.Request)
                        {
                            case ModelExchengeInfoRequest.GetCountThing:
                                {
                                    if (request.Thing == null)
                                    {
                                        return ErrorResponse;
                                    }

                                    var count = data.OrderOperator.CountThingDef(context.Player, request.Thing);

                                    return new ModelExchengeInfo
                                    {
                                        Result = count,
                                        Status = 0,
                                        Message = null
                                    };
                                }
                            default:
                                {
                                    return ErrorResponse;
                                }
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                ExceptionUtil.ExceptionLog(exp, "Server ExchengeInfo login=" + context?.Player?.Public?.Login);
                return ErrorResponse;
            }
        }
    }
}