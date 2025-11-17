using Moq;
using Order_Project.Models;
using Order_Project.Services;
using Order_Project.Services.Intefraces;
using Xunit;

namespace Order_Project_Tests
{
    public class OrderServiceTests
    {
        private readonly Mock<IInventoryService> _inventoryMock;
        private readonly Mock<IPaymentService> _paymentMock;
        private readonly Mock<INotificationService> _notificationMock;
        private readonly OrderService _service;

        public OrderServiceTests()
        {
            _inventoryMock = new Mock<IInventoryService>();
            _paymentMock = new Mock<IPaymentService>();
            _notificationMock = new Mock<INotificationService>();

            _service = new OrderService(
                _inventoryMock.Object,
                _paymentMock.Object,
                _notificationMock.Object
            );
        }

        ///<summary>
        ///test checks the successful creation of an order when
        ///the goods are available and payment has been successfully completed
        ///</summary>
        [Fact]
        public void CreateOrder_Success()
        {
            const string testProduct = "ball";
            const int testQuantity = 2;

            _inventoryMock.Setup(i => i.CheckStock(testProduct, testQuantity)).Returns(true);
            _paymentMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            var resultOrder = _service.CreateOrder(testProduct, testQuantity);

            Assert.NotNull(resultOrder);
            _inventoryMock.Verify(i => i.ReduceStock(testProduct, testQuantity), Times.Once());
            _notificationMock.Verify(n => n.SendConfirmation(resultOrder), Times.Once());
        }

        /// <summary>
        /// Checks whether an InvalidOperationException exception is thrown
        /// if the warehouse does not have sufficient stock
        /// </summary>
        [Fact]
        public void CreateOrder_NotEnoughStock_Fails()
        {
            const string testProduct = "doll";
            const int testQuantity = 5;

            _inventoryMock.Setup(i => i.CheckStock(testProduct, testQuantity)).Returns(false);

            Assert.Throws<InvalidOperationException>(() =>
                _service.CreateOrder(testProduct, testQuantity)
            );

            _paymentMock.Verify(p => p.ProcessPayment(It.IsAny<Order>()), Times.Never());
        }

        /// <summary>
        /// Checks whether an ArgumentException is thrown when attempting to create
        /// an order with negative Quantity values or an empty product name
        /// </summary>
        [Theory]
        [InlineData("pen", 0)]
        [InlineData("pen", -10)]
        [InlineData("", 5)]
        public void CreateOrder_InvalidInput_ThrowsArgumentException(string product, int quantity)
        {
            Assert.Throws<ArgumentException>(() => _service.CreateOrder(product, quantity));

            _inventoryMock.Verify(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        /// <summary>
        /// Checks logic in case of failed payment:
        /// InvalidOperationException and IncreaseStock call to return the goods.
        /// </summary>
        [Fact]
        public void Create_PaymentFails_RollsBack()
        {
            const string testProduct = "car";
            const int testQuantity = 1;

            _inventoryMock.Setup(i => i.CheckStock(testProduct, testQuantity)).Returns(true);
            _paymentMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(false);
            _inventoryMock.Setup(i => i.ReduceStock(testProduct, testQuantity)).Verifiable();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                _service.CreateOrder(testProduct, testQuantity)
            );
            Assert.Equal("Payment failed.", exception.Message);

            _inventoryMock.Verify(i => i.IncreaseStock(testProduct, testQuantity), Times.Once());
            _notificationMock.Verify(n => n.SendConfirmation(It.IsAny<Order>()), Times.Never());
        }

        /// <summary>
        /// Checks the successful update of an existing order to a new valid quantity
        /// </summary>
        [Fact]
        public void Update_Success()
        {
            const int newQuantity = 10;

            _inventoryMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _paymentMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            _service.CreateOrder("widget", 5);

            var updateResult = _service.UpdateOrder(1, newQuantity);

            Assert.True(updateResult);

            var updatedOrder = _service.GetOrders().Find(o => o.Id == 1);
            Assert.Equal(newQuantity, updatedOrder.Quantity);

            Assert.Equal(1, _service.GetOrders().Count);
        }

        /// <summary>
        /// Checks whether UpdateOrder returns false if 
        /// the order is not found or if the new quantity is negative or equal to 0.
        /// </summary>
        [Theory]
        [InlineData(999, 5)]
        [InlineData(1, 0)]
        [InlineData(1, -3)]
        public void Update_Fails(int orderId, int newQuantity)
        {
            _inventoryMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _paymentMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            int initialCallCount = 0;
            if (_service.GetOrders().Count == 0)
            {
                _service.CreateOrder("InitialItem", 1);
                initialCallCount = 1;
            }

            var updateResult = _service.UpdateOrder(orderId, newQuantity);

            Assert.False(updateResult);

            _inventoryMock.Verify(i => i.ReduceStock(It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(initialCallCount));
            _inventoryMock.Verify(i => i.IncreaseStock(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        /// <summary>
        /// Checks the successful deletion of an existing order
        /// </summary>
        [Fact]
        public void Remove_Success()
        {
            const int orderId = 1;
            const string product = "headphones";
            const int quantity = 4;

            _inventoryMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _paymentMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);
            _service.CreateOrder(product, quantity);

            var result = _service.RemoveOrder(orderId);

            Assert.True(result);
            Assert.Empty(_service.GetOrders());
            _inventoryMock.Verify(i => i.IncreaseStock(product, quantity), Times.Once());
        }

        /// <summary>
        /// Checks whether RemoveOrder returns false if the order with the specified ID is not found
        /// </summary>
        [Fact]
        public void Remove_NonExisting_Fails()
        {
            const int nonExistingOrderId = 999;
            Assert.Empty(_service.GetOrders());

            var result = _service.RemoveOrder(nonExistingOrderId);

            Assert.False(result);
            _inventoryMock.Verify(i => i.IncreaseStock(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        /// <summary>
        /// Checks whether GetOrders returns the correct list
        /// </summary>
        [Fact]
        public void GetOrders_ReturnsListState()
        {
            _inventoryMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _paymentMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            var initialOrders = _service.GetOrders();

            Assert.Empty(initialOrders);

            var newOrder = _service.CreateOrder("NewProduct", 1);
            var currentOrders = _service.GetOrders();

            Assert.NotEmpty(currentOrders);
            Assert.Contains(newOrder, currentOrders);
        }

        /// <summary>
        /// Checks that the number of orders in the GetOrders() list after deletion is not equal to the initial number
        /// </summary>
        [Fact]
        public void GetOrders_CountChangesAfterRemoval_AssertNotEqual()
        {
            _inventoryMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _paymentMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            var orderA = _service.CreateOrder("Item A", 1);
            var orderB = _service.CreateOrder("Item B", 2);

            var initialCount = _service.GetOrders().Count; 

            _service.RemoveOrder(orderA.Id);

            var finalCount = _service.GetOrders().Count; 

            Assert.NotEqual(initialCount, finalCount);
            Assert.Equal(1, finalCount);
        }
    }
}
