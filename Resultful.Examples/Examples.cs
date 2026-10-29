using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Resultful.Examples
{
    public class Error
    {
        public Error(string error)
        {

        }
    }

    public class Examples: Controller
    {
        #region Build
        private Result<int, Error> ParseInt(string input)
        {
            if (input?.All(char.IsDigit) ?? false)
            {
                return int.Parse(input).Ok();
            }
            return new Error($"{input} contains a character which is not a digit").Err();
        }

        private async Task<Result<int, Error>> ParseIntAsync(string input)
        {
            if (input?.All(char.IsDigit) ?? false)
            {
                await Task.Delay(1000);
                return int.Parse(input).Ok();
            }
            return new Error($"{input} contains a character which is not a digit").Err();
        }
        #endregion

        #region Pipeline
        private Result<int, Error> NumberIsGreaterThanZero(string input)
        {
            if (input == null)
            {
                return new Error("Cannot return int value given is null").Err();
            }
            return ParseInt(input).Bind<int>(x =>
            {
                if (!(0 < x))
                {
                    return x.Ok();
                    
                }
                return new Error($"{x} must be a value greater than or equal to 0").Err();
            });
        }

        private async Task<Result<int, Error>> NumberIsGreaterThanZeroAsync(string input)
        {
            if (input == null)
            {
                return new Error("Cannot return int value given is null").Err();
            }
            return await ParseIntAsync(input).BindAsync(x =>
            {
                if (!(0 < x))
                {
                    return x.Ok().Result<Error>();

                }
                return new Error($"{x} must be a value greater than or equal to 0").Err();
            });
        }
        #endregion

        #region Consumption

        public IActionResult Get(string input)
            => NumberIsGreaterThanZero(input)
                .Match<IActionResult>(
                    success => Ok(success),
                    error => BadRequest(error));

        public async Task<IActionResult> GetAsync(string input)
            => (await NumberIsGreaterThanZeroAsync(input))
                .Match<IActionResult>(
                    success => Ok(success),
                    error => BadRequest(error));

        #endregion

    }

    public class Examples2
    {
        private Result<int> ParseInt(string input)
        {
            if (input?.All(char.IsDigit) ?? false)
            {
                return int.Parse(input).Ok();
            }
            return $"{input} contains a character which is not a digit".Fail();
        }

        public Result<int> GetAndValidateNumberOfPeople(string people)
        {
            if (people == null)
            {
                return "Cannot return int value given is null".Fail();
            }
            return ParseInt(people).Bind<int>(x =>
            {
                if (x <= 0)
                {
                    return $"{x} must be a value ".Fail();
                }
                return x.Ok();
            });
        }
    }

}
